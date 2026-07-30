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
    // Quest board browse. Per-row buttons (Claim / Submit / Approve / Cancel) shown by quest state + the viewer's
    // relationship to it (poster vs claimer vs third party); toolbar has the Post composer + Refresh
    //
    // Default sort: open first (by PostedUtcTicks desc), then everything else, also desc. Matches "newest activity
    // on top" UX
    public class Dialog_KMHQuestBoard : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(900f, 620f);

        private Vector2  _scroll;

        // Toolbar state. Open-only defaults true so the board starts focused on actionable
        // quests
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
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh);
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

            // Two-row toolbar - 4 checkboxes + filter + 2 right-pinned buttons don't fit one row at 900 px wide.
            //
            // Row 1: filter (left) + Refresh / Post Quest (right).
            const float toolbarBtnW = 110f;
            _filter = DialogLayout.SearchField(new Rect(0f, y, 320f, 28f), _filter, "Filter by title, item, poster…");
            if (IconButton.Draw(new Rect(rect.width - toolbarBtnW, y, toolbarBtnW - 4f, 28f), KMHTextures.Post, "Post Quest…"))
            {
                Find.WindowStack.Add(new Dialog_KMHPostQuest());
            }
            if (Widgets.ButtonText(new Rect(rect.width - toolbarBtnW * 2f, y, toolbarBtnW - 4f, 28f), "Refresh"))
            {
                QuestHandler.RequestSnapshot();
                MarkRefreshed();
            }
            y += 32f;

            // Row 2: scope toggles via DrawTightCheckbox so the ☐ marker sits flush against each label
            float cbx = 0f;
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "My quests",     ref _onlyMine);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Open only",     ref _onlyOpen);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Guild only",    ref _onlyGuild);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Personal only", ref _onlyPersonal);
            y += 30f;

            // Server-driven global quests, shown as a banner above the player-posted list (only when active).
            y = DrawGlobalQuests(rect, y);

            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawQuestList(listBox, s);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        // Banner of active server-driven global quests above the player-posted list. Returns the y to continue at.
        // Drawn only when at least one is active; capped at a few rows so the player list keeps the space
        private float DrawGlobalQuests(Rect rect, float y)
        {
            List<Features.World.Dto.ServerQuestDto> gqs = Features.World.WorldCache.ActiveServerQuests();
            if (gqs.Count == 0) return y;

            const float rowH = 38f;
            int   shown  = Mathf.Min(gqs.Count, 3);
            float panelH = 24f + shown * (rowH + 2f) + 4f;
            Rect  panel  = new Rect(0f, y, rect.width, panelH);
            Widgets.DrawMenuSection(panel);

            float px = panel.x + 8f, pw = panel.width - 16f, py = panel.y + 4f;
            Color old = GUI.color;
            GUI.color = new Color(0.886f, 0.757f, 0.420f); // gold
            DialogLayout.LabelTrunc(new Rect(px, py, pw, 18f),
                "<b>Global Quests</b>" + (gqs.Count > shown ? $"  <color=grey>(+{gqs.Count - shown} more)</color>" : ""));
            GUI.color = old;
            py += 22f;

            string me  = KmhSession.Me;
            long   now = DateTime.UtcNow.Ticks;
            for (int i = 0; i < shown; i++)
            {
                DrawGlobalQuestRow(new Rect(px, py, pw, rowH), gqs[i], me, now);
                py += rowH + 2f;
            }
            return y + panelH + 6f;
        }

        private static void DrawGlobalQuestRow(Rect row, Features.World.Dto.ServerQuestDto q, string me, long now)
        {
            bool   comp    = string.Equals(q.Kind, Features.World.Dto.ServerQuestDto.KindCompetitive, StringComparison.OrdinalIgnoreCase);
            bool   deliver = string.Equals(q.Objective, Features.World.Dto.ServerQuestDto.ObjDeliver, StringComparison.OrdinalIgnoreCase);
            string kindTag = comp ? "<color=#F5C242>RACE</color>" : "<color=#7CD37C>CO-OP</color>";
            string objVerb = string.Equals(q.Objective, Features.World.Dto.ServerQuestDto.ObjBuild, StringComparison.OrdinalIgnoreCase) ? "Build"
                           : deliver ? "Deliver" : "Hunt";
            string reward  = q.RewardPool > 0 ? $"<color=yellow>{SilverFmt.Format(q.RewardPool)}</color>" : "<color=grey>glory</color>";
            string time    = q.EndsUtcTicks > 0 ? $"  <color=grey>•</color>  {FormatTimeRemaining(q.EndsUtcTicks, now)}" : "";

            // Line 1: kind + title + reward + time remaining
            DialogLayout.LabelTrunc(new Rect(row.x, row.y, row.width, 18f),
                $"[{kindTag}] <b>{q.Title}</b>  <color=grey>•</color> {reward}{time}");

            // Line 2: progress bar with goal label, this player's contribution, and a Deliver button on deliver quests
            int mine = 0;
            if (!string.IsNullOrEmpty(me) && q.Contributors != null) q.Contributors.TryGetValue(me, out mine);
            float pct = q.GoalQty > 0 ? Mathf.Clamp01((float)q.ProgressQty / q.GoalQty) : 0f;

            float rightW = deliver ? 168f : 110f;
            Rect bar = new Rect(row.x, row.y + 20f, row.width - rightW, 14f);
            Widgets.DrawBoxSolid(bar, new Color(0.16f, 0.16f, 0.16f));
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * pct, bar.height),
                comp ? new Color(0.96f, 0.76f, 0.26f) : new Color(0.34f, 0.55f, 0.45f));

            GameFont    pf = Text.Font;   Text.Font   = GameFont.Tiny;
            TextAnchor  pa = Text.Anchor; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, $"{q.ProgressQty}/{q.GoalQty} {objVerb} {q.TargetDefName}");
            Text.Anchor = pa; Text.Font = pf;

            Color old = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(bar.xMax + 8f, row.y + 18f, deliver ? 58f : 102f, 18f),
                mine > 0 ? $"you: <color=white>{mine}</color>" : "<color=grey>you: 0</color>");
            GUI.color = old;

            if (deliver)
            {
                Rect btn = new Rect(row.xMax - 66f, row.y + 16f, 66f, 20f);
                if (Widgets.ButtonText(btn, "Deliver…"))
                {
                    long   id     = q.Id;
                    string target = q.TargetDefName;
                    var    car    = CaravanReader.GetSelectedCaravan();
                    var    def    = ColonyGoods.Def(target);
                    // Cap to what the source actually holds: the selected caravan, else the colony's stockpiles.
                    int    have   = def == null ? 0
                                  : (car != null ? ColonyGoods.Count(car, def)
                                                 : ColonyGoods.CountOnMap(ColonyGoods.DepositMap(), def));
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title:        $"Deliver to {q.Title}",
                        confirmLabel: "Deliver",
                        unitLabel:    def != null ? def.label : target,
                        maxHint:      have,
                        onConfirm:    n => Features.World.WorldHandler.TryDeliver(id, target, n)));
                }
            }
        }

        private void DrawQuestList(Rect box, QuestSnapshot s)
        {
            const float rowH = 64f;

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

                // Sort: open quests first (newest-first within), then everything else (also newest-first).
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

            // Line 1: title + state + kind + poster + ownership badge
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

            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, textW, 18f),
                $"<b>#{q.Id}  {q.Title}</b>  {stateTag}{visTag}  <color=grey>{kindTag} • by</color> {poster}{ReputationCache.Badge(q.PosterUsername)} <color=grey>•</color> {ownership}");

            // Line 2: detail (per-kind summary OR description-trimmed)
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
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 18f, textW, 18f), detail);
            GUI.color = oldCol;

            // Line 3: bounty + claimed-by + expiry
            string bounty = q.BountySilver > 0 ? $"<color=yellow>{SilverFmt.Format(q.BountySilver)}</color>" : "<color=grey>no silver</color>";
            int extraItems = q.BountyItems?.Count ?? 0;
            if (extraItems > 0) bounty += $" + {extraItems} item type(s)";

            string claimedBy = !string.IsNullOrEmpty(q.ClaimedByUsername)
                ? $"  •  claimed by {LinkedAccountsCache.Format(q.ClaimedByUsername)}"
                : "";
            string time = q.State == QuestEntry.StateOpen
                ? $"  •  expires in {FormatTimeRemaining(q.ExpiresUtcTicks, nowTicks)}"
                : "";

            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 36f, textW, 18f),
                $"Bounty: {bounty}{claimedBy}{time}");
            GUI.color = oldCol;

            // Per-row action buttons. Visibility depends on caller's role: Open + not poster -> Claim Open + is
            // poster -> Cancel Claimed/Submitted + is claimer (any kind) -> Submit (DeliverItem auto-completes
            // server-side via the treasury check; Bounty enters Submitted state) Submitted + is poster + Bounty
            // kind -> Approve (poster signs off; server pays the bounty + marks Completed)
            // Authoritative state lands in the next kmh.quest.snapshot push;
            // the buttons just fire mutation envelopes.
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
                // Top: kind-specific completion action (only while Claimed).
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
                // Bottom: abandon (drop the claim; costs reputation server-side).
                if (IconButton.Draw(bottomBtn, KMHTextures.Cancel, "Abandon")) QuestHandler.TryAbandon(q.Id);
            }
            else if (isPoster && q.State == QuestEntry.StateSubmitted && q.Kind == QuestEntry.KindBounty)
            {
                if (IconButton.Draw(topBtn, KMHTextures.Approve, "Approve")) QuestHandler.TryApprove(q.Id);
            }
            else if (isPoster && q.State == QuestEntry.StatePendingReview)
            {
                // Custom proof awaiting review: approve pays out, reject returns it to the board (claimer takes a
                // reputation hit)
                if (IconButton.Draw(topBtn, KMHTextures.Approve, "Review"))
                {
                    long id = q.Id;
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                    {
                        new FloatMenuOption("Approve & pay bounty", () => QuestHandler.TryReview(id, true, "")),
                        new FloatMenuOption("Reject (with note)…", () =>
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

        private static string FormatTimeRemaining(long expiresUtcTicks, long nowTicks)
        {
            if (expiresUtcTicks <= 0) return "never";
            if (nowTicks >= expiresUtcTicks) return "<color=#ff8080>expired</color>";
            try
            {
                TimeSpan span = TimeSpan.FromTicks(expiresUtcTicks - nowTicks);
                if (span.TotalDays    >= 1) return $"{(int)span.TotalDays}d";
                if (span.TotalHours   >= 1) return $"{(int)span.TotalHours}h";
                if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
                return $"{(int)span.TotalSeconds}s";
            }
            catch { return "?"; }
        }
    }
}
