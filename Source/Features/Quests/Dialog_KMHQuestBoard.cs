using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Quests.Dto;
using KMHPatch.Features.Reputation;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Quests
{
    // Quest board; per-row buttons follow quest state and whether the viewer is poster, claimer or third party.
    public class Dialog_KMHQuestBoard : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(900f, 620f);

        private Vector2  _scroll;

        // Open-only defaults true so the board starts on actionable quests.
        private string _filter        = "";
        private bool   _onlyMine      = false;
        private bool   _onlyOpen      = true;
        private bool   _onlyGuild     = false;
        private bool   _onlyPersonal  = false;

        private List<QuestEntry> _visible;
        private object           _visibleSource;
        private string           _visibleFilter;
        private bool             _vcMine, _vcOpen, _vcGuild, _vcPersonal;

        public Dialog_KMHQuestBoard()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            EnableAutoRefresh(() => { QuestHandler.RequestSnapshot(); Features.World.WorldHandler.RequestSnapshot(); });
            ReputationCache.RequestSnapshot();
            QuestCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            QuestCache.Updated -= OnSnapshotUpdated;
        }

        // Also invalidate the cached filtered view so the fresh snapshot rebuilds it.
        private void OnSnapshotUpdated()
        {
            _visible = null;
            MarkRefreshed();
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Quest Board");
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (!QuestCache.HasSnapshot)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 20f),
                    "<color=grey>Loading quest board…</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            QuestSnapshot s = QuestCache.Snapshot;

            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                $"Posted: {s.LifetimeQuestsPosted}  |  " +
                $"Completed: {s.LifetimeQuestsCompleted}  |  " +
                $"Lifetime bounty: {SilverFmt.Format(s.LifetimeBountySilverPaid)}");
            GUI.color = oldCol;
            y += 24f;

            // Filter width is clamped, not fixed: against right-anchored buttons it overlaps below about 540px.
            float toolbarBtnW = Mathf.Max(IconButton.WidthFor("Refresh", false), IconButton.WidthFor("Post Quest", true));
            float filterW = Mathf.Clamp(rect.width - toolbarBtnW * 2f - 12f, 120f, 320f);
            _filter = DialogLayout.SearchField(new Rect(0f, y, filterW, 28f), _filter, "Filter by title, item, poster");
            if (IconButton.Draw(new Rect(rect.width - toolbarBtnW, y, toolbarBtnW - 4f, 28f), KMHTextures.Post, "Post Quest"))
            {
                Find.WindowStack.Add(new Dialog_KMHPostQuest());
            }
            if (Widgets.ButtonText(new Rect(rect.width - toolbarBtnW * 2f, y, toolbarBtnW - 4f, 28f), "Refresh"))
            {
                QuestHandler.RequestSnapshot();
            }
            y += 32f;

            float cbx = 0f;
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "My quests",     ref _onlyMine);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Open only",     ref _onlyOpen);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Guild only",    ref _onlyGuild);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Personal only", ref _onlyPersonal);
            y += DialogLayout.ToolbarRowH;

            // Server-driven global quests, shown as a banner above the player-posted list (only when active).
            y = DrawGlobalQuests(rect, y);

            Rect listBox = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(listBox);
            DrawQuestList(listBox, s);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private float DrawGlobalQuests(Rect rect, float y)
        {
            List<Features.World.Dto.ServerQuestDto> gqs = Features.World.WorldCache.ActiveServerQuests();
            if (gqs.Count == 0) return y;

            // Asked, never copied: a local 38f duplicate of this clipped every banner row once the row became measured.
            float rowH   = Features.World.GlobalQuestRow.Height;
            int   shown  = Mathf.Min(gqs.Count, 3);
            float panelH = 24f + shown * (rowH + 2f) + 4f;
            Rect  panel  = new Rect(0f, y, rect.width, panelH);
            Widgets.DrawMenuSection(panel);

            float px = panel.x + 8f, pw = panel.width - 16f, py = panel.y + 4f;
            Color old = GUI.color;
            GUI.color = new Color(0.886f, 0.757f, 0.420f);
            DialogLayout.LabelTrunc(new Rect(px, py, Mathf.Max(0f, pw - 90f), DialogLayout.TextRowH),
                "<b>Global Quests</b>" + (gqs.Count > shown ? $"  <color=grey>(+{gqs.Count - shown} more)</color>" : ""));
            GUI.color = old;

            // Explicit route to the full list - this banner is capped at a few rows.
            if (Widgets.ButtonText(new Rect(px + pw - 86f, py - 2f, 86f, 20f), "All in World"))
                Find.WindowStack.Add(new Features.World.Dialog_KMHWorld());
            py += 22f;

            string me  = KmhSession.Me;
            long   now = DateTime.UtcNow.Ticks;
            for (int i = 0; i < shown; i++)
            {
                Features.World.GlobalQuestRow.Draw(new Rect(px, py, pw, rowH), gqs[i], me, now);
                py += rowH + 2f;
            }
            return y + panelH + 6f;
        }

        private void DrawQuestList(Rect box, QuestSnapshot s)
        {
            // Three measured text lines. At 64 with an 18px pitch each line overlapped the one below it.
            float rowH = DialogLayout.TextRowsH(3, 10f);

            string mine        = KmhSession.Me;
            string filterLower = (_filter ?? "").Trim().ToLower();

            bool inputsChanged =
                _visible == null
                || !ReferenceEquals(_visibleSource, s.Quests)
                || _visibleFilter != filterLower
                || _vcMine        != _onlyMine
                || _vcOpen        != _onlyOpen
                || _vcGuild       != _onlyGuild
                || _vcPersonal    != _onlyPersonal;

            if (inputsChanged)
            {
                List<QuestEntry> source = s.Quests ?? new List<QuestEntry>();
                List<QuestEntry> filtered = new List<QuestEntry>(source.Count);

                for (int j = 0; j < source.Count; j++)
                {
                    QuestEntry q = source[j];
                    if (q == null) continue;

                    if (_onlyMine)
                    {
                        bool isPoster  = KmhSession.Same(q.PosterUsername,    mine);
                        bool isClaimer = KmhSession.Same(q.ClaimedByUsername, mine);
                        if (!isPoster && !isClaimer) continue;
                    }
                    if (_onlyOpen && q.State != QuestEntry.StateOpen) continue;

                    bool isGuildPosted =
                        !string.IsNullOrEmpty(q.PosterTreasuryKey) &&
                        !q.PosterTreasuryKey.StartsWith("_personal:", StringComparison.OrdinalIgnoreCase);
                    if (_onlyGuild    && !isGuildPosted) continue;
                    if (_onlyPersonal &&  isGuildPosted) continue;

                    if (filterLower.Length > 0)
                    {
                        string title  = (q.Title             ?? "").ToLower();
                        string item   = (q.TargetItemDefName ?? "").ToLower();
                        string poster = (q.PosterUsername    ?? "").ToLower();
                        if (!title.Contains(filterLower) && !item.Contains(filterLower) && !poster.Contains(filterLower))
                            continue;
                    }

                    filtered.Add(q);
                }

                filtered.Sort((a, b) =>
                {
                    bool aOpen = a.State == QuestEntry.StateOpen;
                    bool bOpen = b.State == QuestEntry.StateOpen;
                    if (aOpen != bOpen) return aOpen ? -1 : 1;
                    return b.PostedUtcTicks.CompareTo(a.PostedUtcTicks);
                });

                _visible       = filtered;
                _visibleSource = s.Quests;
                _visibleFilter = filterLower;
                _vcMine        = _onlyMine;
                _vcOpen        = _onlyOpen;
                _vcGuild       = _onlyGuild;
                _vcPersonal    = _onlyPersonal;
            }

            List<QuestEntry> rows = _visible;
            long now = DateTime.UtcNow.Ticks;
            // 'mine' already computed above for the filter pass; reused here for the per-row 'is me?' check

            string empty = (s.Quests == null || s.Quests.Count == 0)
                ? "<color=grey>No quests posted yet. Be the first!</color>"
                : "<color=grey>No quests match the filter.</color>";

            DialogLayout.ScrollList(box, ref _scroll, rows.Count, rowH,
                (i, row) => DrawQuestRow(row, rows[i], now, mine), empty);
        }

        private static void DrawQuestRow(Rect row, QuestEntry q, long nowTicks, string mine)
        {
            Rect inner = row.ContractedBy(6f);
            // Right edge reserved for two stacked action buttons
            const float reservedRight = 110f;
            float textW = inner.width - reservedRight;

            string stateTag = StateTag(q.State);
            string kindTag  = KindTag(q.Kind);

            bool isGuildPosted =
                !string.IsNullOrEmpty(q.PosterTreasuryKey) &&
                !q.PosterTreasuryKey.StartsWith("_personal:", StringComparison.OrdinalIgnoreCase);
            string ownership = isGuildPosted
                ? $"<color=#79b8ff>{q.PosterTreasuryKey}</color>"
                : "<color=#cccccc>personal</color>";
            string visTag = q.Visibility == QuestEntry.VisibilityGuildOnly
                ? "  <color=#ffce4d>guild-only</color>"
                : "";

            string poster = LinkedAccountsCache.Format(q.PosterUsername);   // Format() already renders empty as "(unknown)"

            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, textW, DialogLayout.TextRowH),
                $"<b>#{q.Id}  {q.Title}</b>  {stateTag}{visTag}  <color=grey>{kindTag} • by</color> {poster}{ReputationCache.Badge(q.PosterUsername)} <color=grey>•</color> {ownership}");

            string detail;
            switch (q.Kind)
            {
                case QuestEntry.KindDeliverItem:
                    detail = $"Deliver {q.TargetItemQty}× {ItemLabels.ResolveLabel(q.TargetItemDefName)}" +
                             (q.TargetQualityIndex > 0 ? $" ({UI.ItemKeys.QualityName(q.TargetQualityIndex)} or better)" : "");
                    if (!string.IsNullOrEmpty(q.TargetTreasuryKey)) detail += $"  →  {q.TargetTreasuryKey}";
                    break;
                case QuestEntry.KindEscort:
                    detail = $"Escort {(!string.IsNullOrEmpty(q.EscortTargetDescription) ? q.EscortTargetDescription : "target")}  (tile {q.EscortPickupTile} → {q.EscortDropoffTile})";
                    break;
                case QuestEntry.KindDefend:
                    detail = $"Defend tile {q.DefendColonyTile} for {q.DefendDurationGameTicks / 60000L} day(s)";
                    break;
                case QuestEntry.KindHunt:
                    detail = $"Hunt {q.HuntTargetCount}× {q.HuntTargetDefName}";
                    break;
                case QuestEntry.KindBuild:
                    detail = $"Build {q.BuildCount}× {q.BuildStructureDefName} at tile {q.BuildAtTile}";
                    break;
                case QuestEntry.KindCustom:
                    detail = q.ReviewState == QuestEntry.ReviewPending ? "Custom · proof submitted, awaiting review" : "Custom · poster-reviewed";
                    break;
                default:
                    detail = "Bounty";
                    break;
            }
            string desc = q.Description ?? "";
            if (desc.Length > 0)
            {
                string trimmed = desc.Length > 90 ? desc.Substring(0, 90) + "…" : desc;
                detail += "  ·  " + trimmed;
            }
            Color oldCol = GUI.color;
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH, textW, DialogLayout.TextRowH), detail);
            GUI.color = oldCol;

            string bounty = q.BountySilver > 0 ? $"<color=yellow>{SilverFmt.Format(q.BountySilver)}</color>" : "<color=grey>no silver</color>";
            int extraItems = q.BountyItems?.Count ?? 0;
            if (extraItems > 0) bounty += $" + {extraItems} item type(s)";

            string claimedBy = !string.IsNullOrEmpty(q.ClaimedByUsername)
                ? $"  •  claimed by {LinkedAccountsCache.Format(q.ClaimedByUsername)}"
                : "";
            string time = q.State == QuestEntry.StateOpen
                ? $"  •  expires in {DialogLayout.TimeRemainingShort(q.ExpiresUtcTicks, nowTicks)}"
                : "";

            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 2f, textW, DialogLayout.TextRowH),
                $"Bounty: {bounty}{claimedBy}{time}");
            GUI.color = oldCol;

            // These only fire mutation envelopes; authoritative state lands in the next snapshot push.
            float btnW = 100f;
            float btnsX = inner.xMax - btnW;
            Rect topBtn    = new Rect(btnsX, inner.y + 4f, btnW, 24f);
            Rect bottomBtn = new Rect(btnsX, inner.y + 32f, btnW, 24f);

            bool isPoster  = KmhSession.Same(q.PosterUsername,   mine);
            bool isClaimer = KmhSession.Same(q.ClaimedByUsername, mine);

            if (q.State == QuestEntry.StateOpen)
            {
                if (!isPoster)
                {
                    if (IconButton.Draw(topBtn, KMHTextures.Claim, "Claim")) QuestHandler.TryClaim(q.Id);
                }
                else
                {
                    if (IconButton.Draw(topBtn, KMHTextures.Cancel, "Cancel")) QuestHandler.TryCancel(q.Id);
                }
            }
            else if (isClaimer && (q.State == QuestEntry.StateClaimed
                                   || q.State == QuestEntry.StateSubmitted
                                   || q.State == QuestEntry.StatePendingReview))
            {
                if (q.State == QuestEntry.StateClaimed)
                {
                    switch (q.Kind)
                    {
                        case QuestEntry.KindDeliverItem:
                        case QuestEntry.KindBounty:
                            if (IconButton.Draw(topBtn, KMHTextures.Submit, "Submit")) QuestHandler.TrySubmit(q.Id);
                            break;
                        case QuestEntry.KindCustom:
                            if (IconButton.Draw(topBtn, KMHTextures.Submit, "Proof"))
                                Find.WindowStack.Add(new Dialog_KMHMultilineTextInput(
                                    title:        "Submit proof for review",
                                    confirmLabel: "Submit",
                                    initial:      "",
                                    maxChars:     1024,
                                    rejectEmpty:  true,
                                    onConfirm:    s => QuestHandler.TrySubmitProof(q.Id, s, "")));
                            break;
                        default: // escort / defend / hunt / build -> auto-verify report
                            if (IconButton.Draw(topBtn, KMHTextures.Submit, "Report")) QuestHandler.TryVerify(q.Id);
                            break;
                    }
                }
                // Abandoning costs reputation, server-side.
                if (IconButton.Draw(bottomBtn, KMHTextures.Cancel, "Abandon")) QuestHandler.TryAbandon(q.Id);
            }
            else if (isPoster && q.State == QuestEntry.StateSubmitted && q.Kind == QuestEntry.KindBounty)
            {
                if (IconButton.Draw(topBtn, KMHTextures.Approve, "Approve")) QuestHandler.TryApprove(q.Id);
            }
            else if (isPoster && q.State == QuestEntry.StatePendingReview)
            {
                // Rejecting returns the quest to the board and costs the claimer reputation.
                if (IconButton.Draw(topBtn, KMHTextures.Approve, "Review"))
                {
                    long id = q.Id;
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                    {
                        new FloatMenuOption("Approve & pay bounty", () => QuestHandler.TryReview(id, true, "")),
                        new FloatMenuOption("Reject (with note)", () =>
                            Find.WindowStack.Add(new Dialog_KMHMultilineTextInput(
                                title:        "Reason for rejection",
                                confirmLabel: "Reject",
                                initial:      "",
                                maxChars:     256,
                                rejectEmpty:  false,
                                onConfirm:    s => QuestHandler.TryReview(id, false, s)))),
                    }));
                }
            }
        }

        private static string StateTag(string state)
        {
            switch (state)
            {
                case QuestEntry.StateOpen:      return "<color=#80ff80>OPEN</color>";
                case QuestEntry.StateClaimed:   return "<color=yellow>CLAIMED</color>";
                case QuestEntry.StateSubmitted: return "<color=cyan>SUBMITTED</color>";
                case QuestEntry.StateCompleted: return "<color=grey>DONE</color>";
                case QuestEntry.StateCancelled: return "<color=grey>CANCELLED</color>";
                case QuestEntry.StateExpired:   return "<color=grey>EXPIRED</color>";
                case QuestEntry.StatePendingReview: return "<color=#ffce4d>REVIEW</color>";
                default:                        return string.IsNullOrEmpty(state) ? "?" : state.ToUpper();
            }
        }

        private static string KindTag(string kind)
        {
            switch (kind)
            {
                case QuestEntry.KindBounty: return "Bounty";
                case QuestEntry.KindEscort: return "Escort";
                case QuestEntry.KindDefend: return "Defend";
                case QuestEntry.KindHunt:   return "Hunt";
                case QuestEntry.KindBuild:  return "Build";
                case QuestEntry.KindCustom: return "Custom";
                default:                    return "Deliver";
            }
        }
    }
}
