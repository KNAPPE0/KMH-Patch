using System;
using System.Collections.Generic;
using GameClient.Misc;
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
        private float    _refreshTimer   = DialogLayout.AutoRefreshSeconds;
        private DateTime _lastRefreshUtc = DateTime.UtcNow;

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

            QuestHandler.RequestSnapshot();
            ReputationCache.RequestSnapshot();
            _lastRefreshUtc   = DateTime.UtcNow;
            QuestCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            QuestCache.Updated -= OnSnapshotUpdated;
        }

        private void OnSnapshotUpdated()
        {
            _visible        = null;
            _lastRefreshUtc = DateTime.UtcNow;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                QuestHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Quest Board");
            int secsSince = Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds);
            DialogLayout.DrawLiveBadge(rect, secsSince);
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
                _lastRefreshUtc = DateTime.UtcNow;
            }
            y += 32f;

            // Row 2: scope toggles via DrawTightCheckbox so the ☐ marker sits flush against each label
            float cbx = 0f;
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "My quests",     ref _onlyMine);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Open only",     ref _onlyOpen);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Guild only",    ref _onlyGuild);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Personal only", ref _onlyPersonal);
            y += 30f;

            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawQuestList(listBox, s);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void DrawQuestList(Rect box, QuestSnapshot s)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 64f;

            string mine        = SessionHandler.Username ?? string.Empty;
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
                        bool isPoster  = !string.IsNullOrEmpty(mine) && string.Equals(q.PosterUsername,    mine, StringComparison.OrdinalIgnoreCase);
                        bool isClaimer = !string.IsNullOrEmpty(mine) && string.Equals(q.ClaimedByUsername, mine, StringComparison.OrdinalIgnoreCase);
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
            float viewH    = Mathf.Max(inner.height, rows.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float ly  = 0f;
            long  now = DateTime.UtcNow.Ticks;
            // 'mine' already computed above for the filter pass; reused here for the per-row 'is me?' check

            for (int i = 0; i < rows.Count; i++)
            {
                QuestEntry q = rows[i];
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                DrawQuestRow(row, q, now, mine);
                ly += rowH;
            }
            if (rows.Count == 0)
            {
                bool noData = s.Quests == null || s.Quests.Count == 0;
                string msg = noData
                    ? "<color=grey>No quests posted yet. Be the first!</color>"
                    : "<color=grey>No quests match the filter.</color>";
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f), msg);
            }

            Widgets.EndScrollView();
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

            string poster = string.IsNullOrEmpty(q.PosterUsername)
                ? "<color=grey>(unknown)</color>"
                : LinkedAccountsCache.Format(q.PosterUsername);

            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, textW, 18f),
                $"<b>#{q.Id}  {q.Title}</b>  {stateTag}{visTag}  <color=grey>{kindTag} • by</color> {poster}{RepBadge(q.PosterUsername)} <color=grey>•</color> {ownership}");

            // Line 2: detail (per-kind summary OR description-trimmed)
            string detail;
            switch (q.Kind)
            {
                case QuestEntry.KindDeliverItem:
                    detail = $"Deliver {q.TargetItemQty}× {ItemLabels.ResolveLabel(q.TargetItemDefName)}";
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

            bool isPoster  = !string.IsNullOrEmpty(mine) && string.Equals(q.PosterUsername,   mine, StringComparison.OrdinalIgnoreCase);
            bool isClaimer = !string.IsNullOrEmpty(mine) && string.Equals(q.ClaimedByUsername, mine, StringComparison.OrdinalIgnoreCase);

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

        // Tier badge after a username. Neutral / unknown players get nothing, so the board only calls out the
        // trusted and the unreliable
        private static string RepBadge(string username)
        {
            switch (ReputationCache.TierFor(username))
            {
                case "Trusted":    return " <color=#80ff80>[Trusted]</color>";
                case "Unreliable": return " <color=#ff8080>[Unreliable]</color>";
                default:           return "";
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
