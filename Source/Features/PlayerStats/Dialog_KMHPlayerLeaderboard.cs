using System;
using System.Collections.Generic;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.PlayerStats
{
    // Per-player lifetime leaderboard: 9 columns, auto-refresh, a "★" self-marker, and a toolbar with filter / sort
    // dropdown / Linked-only + My guild only toggles + Refresh.
    //
    // Default sort: economy_score desc.
    public class Dialog_KMHPlayerLeaderboard : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(1200f, 620f);

        private Vector2  _scroll = Vector2.zero;
        private float    _refreshTimer  = DialogLayout.AutoRefreshSeconds;
        private DateTime _lastRefreshUtc = DateTime.UtcNow;

        // Toolbar state - filter / sort mode / scope toggles, so players can find specific names + sort by any
        // metric.
        private string   _filter         = "";
        private SortMode _sort           = SortMode.EconomyScore;
        private bool     _onlyLinked     = false;
        private bool     _onlyMyGuild    = false;

        // Cached filtered+sorted view so we don't run OrderByDescending on every redraw. Invalidated on snapshot
        // arrival OR when any toolbar input changes
        private List<PlayerLeaderboardEntry> _visible;
        private List<PlayerLeaderboardEntry> _visibleSource;
        private string                       _visibleFilter;
        private SortMode                     _visibleSort;
        private bool                         _visibleOnlyLinked;
        private bool                         _visibleOnlyMyGuild;

        private enum SortMode
        {
            EconomyScore,
            SilverDonated,
            SalesEarned,
            QuestsCompleted,
            SitesBuilt,
            WorkerXp,
            Tenure
        }

        public Dialog_KMHPlayerLeaderboard()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            // Kick a fresh request the moment we open - don't wait for the first 8s tick
            PlayerStatsHandler.RequestSnapshot();
            _lastRefreshUtc = DateTime.UtcNow;

            // React the instant a server-pushed snapshot lands.
            PlayerStatsCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            PlayerStatsCache.Updated -= OnSnapshotUpdated;
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
                PlayerStatsHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Player Leaderboard");

            int secsSince = Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds);
            DialogLayout.DrawLiveBadge(rect, secsSince);
            DialogLayout.DrawSectionDivider(rect, ref y);

            // Toolbar row: filter (left) + sort dropdown + scope toggles + Refresh (right).
            _filter = DialogLayout.SearchField(new Rect(0f, y, 220f, 28f), _filter, "Filter by player or guild…");

            if (Widgets.ButtonText(new Rect(228f, y, 200f, 28f), $"Sort: {DialogLayout.FriendlyEnumName(_sort)}"))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                foreach (SortMode m in Enum.GetValues(typeof(SortMode)))
                {
                    SortMode captured = m;
                    opts.Add(new FloatMenuOption(DialogLayout.FriendlyEnumName(captured),
                        () => _sort = captured));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            float cbx = 436f;
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Linked only",   ref _onlyLinked);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "My guild only", ref _onlyMyGuild);

            if (Widgets.ButtonText(new Rect(rect.width - 110f, y, 100f, 28f), "Refresh"))
            {
                PlayerStatsHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
            y += 34f;

            DrawHeader(new Rect(0f, y, rect.width, 22f));
            y += 24f;

            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawRows(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private static void DrawHeader(Rect r)
        {
            float[] cols = ColumnXs(r.width);
            DialogLayout.LabelTrunc(new Rect(cols[0], r.y, cols[1] - cols[0], r.height), "<b>Player</b>");
            DialogLayout.LabelTrunc(new Rect(cols[1], r.y, cols[2] - cols[1], r.height), "<b>Guild</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[2], r.y, cols[3] - cols[2], r.height), "<b>Score</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[3], r.y, cols[4] - cols[3], r.height), "<b>Donated</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[4], r.y, cols[5] - cols[4], r.height), "<b>Sales</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[5], r.y, cols[6] - cols[5], r.height), "<b>Quests</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[6], r.y, cols[7] - cols[6], r.height), "<b>Sites</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[7], r.y, cols[8] - cols[7], r.height), "<b>XP</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[8], r.y, r.width - cols[8], r.height), "<b>Tenure</b>");
        }

        private void DrawRows(Rect box)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = DialogLayout.RowHeightSingle;

            // Best-effort "me" detection. Future: pull from RWT's SessionHandler.Username once we wire that read
            // into the patch mod proper
            string mine = string.Empty;

            // Best-effort: 'My guild only' filters against the cached guild's member list. Empty when caller isn't
            // in a guild or the guild snapshot hasn't arrived yet - toggle stays valid but excludes everything in
            // that state
            HashSet<string> myGuildMembers = null;
            if (_onlyMyGuild)
            {
                GuildSnapshot myGuild = GuildCache.Guild;
                if (myGuild?.Members != null)
                {
                    myGuildMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (GuildMemberDto m in myGuild.Members) myGuildMembers.Add(m.Username);
                }
            }

            string filterLower = (_filter ?? "").Trim().ToLower();

            bool inputsChanged =
                _visible == null
                || !ReferenceEquals(_visibleSource, PlayerStatsCache.Entries)
                || _visibleFilter      != filterLower
                || _visibleSort        != _sort
                || _visibleOnlyLinked  != _onlyLinked
                || _visibleOnlyMyGuild != _onlyMyGuild;

            if (inputsChanged)
            {
                List<PlayerLeaderboardEntry> source = PlayerStatsCache.Entries;
                List<PlayerLeaderboardEntry> filtered = new List<PlayerLeaderboardEntry>(source.Count);

                for (int j = 0; j < source.Count; j++)
                {
                    PlayerLeaderboardEntry e = source[j];
                    if (_onlyLinked && !e.IsLinkedToDiscord) continue;
                    if (_onlyMyGuild && (myGuildMembers == null || !myGuildMembers.Contains(e.Username))) continue;
                    if (filterLower.Length > 0)
                    {
                        string user  = (e.Username  ?? "").ToLower();
                        string gname = (e.GuildName ?? "").ToLower();
                        if (!user.Contains(filterLower) && !gname.Contains(filterLower)) continue;
                    }
                    filtered.Add(e);
                }

                switch (_sort)
                {
                    case SortMode.EconomyScore:    filtered.Sort((a, b) => b.EconomyScore.CompareTo(a.EconomyScore)); break;
                    case SortMode.SilverDonated:   filtered.Sort((a, b) => b.SilverDonated.CompareTo(a.SilverDonated)); break;
                    case SortMode.SalesEarned:     filtered.Sort((a, b) => b.SalesEarned.CompareTo(a.SalesEarned)); break;
                    case SortMode.QuestsCompleted: filtered.Sort((a, b) => b.QuestsCompleted.CompareTo(a.QuestsCompleted)); break;
                    case SortMode.SitesBuilt:      filtered.Sort((a, b) => b.SitesBuilt.CompareTo(a.SitesBuilt)); break;
                    case SortMode.WorkerXp:        filtered.Sort((a, b) => b.WorkerXp.CompareTo(a.WorkerXp)); break;
                    case SortMode.Tenure:
                        // Earliest first-seen first (oldest members on top).
                        filtered.Sort((a, b) =>
                        {
                            long aa = a.FirstSeenUtcTicks > 0 ? a.FirstSeenUtcTicks : long.MaxValue;
                            long bb = b.FirstSeenUtcTicks > 0 ? b.FirstSeenUtcTicks : long.MaxValue;
                            return aa.CompareTo(bb);
                        });
                        break;
                }

                _visible             = filtered;
                _visibleSource       = source;
                _visibleFilter       = filterLower;
                _visibleSort         = _sort;
                _visibleOnlyLinked   = _onlyLinked;
                _visibleOnlyMyGuild  = _onlyMyGuild;
            }

            List<PlayerLeaderboardEntry> rows = _visible;
            float viewH    = Mathf.Max(inner.height, rows.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float ly       = 0f;
            float[] cols   = ColumnXs(viewRect.width);

            for (int i = 0; i < rows.Count; i++)
            {
                PlayerLeaderboardEntry r = rows[i];
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);
                TooltipHandler.TipRegion(row, "Click for the full stat card");
                if (Widgets.ButtonInvisible(row)) Find.WindowStack.Add(new Dialog_KMHPlayerCard(r.Username));

                bool isMe = !string.IsNullOrEmpty(mine) && string.Equals(r.Username, mine, StringComparison.OrdinalIgnoreCase);
                string rank = i < 3
                    ? $"<color=yellow>#{i + 1}</color>"
                    : $"<color=grey>#{i + 1}</color>";
                string nameRender = isMe
                    ? $"<color=#80ff80>★</color> {LinkedAccountsCache.Format(r.Username)}"
                    : LinkedAccountsCache.Format(r.Username);

                DialogLayout.LabelTrunc(new Rect(cols[0], ly + 2f, cols[1] - cols[0], rowH - 4f), $"{rank}  <b>{nameRender}</b>");
                DialogLayout.LabelTrunc(new Rect(cols[1], ly + 2f, cols[2] - cols[1], rowH - 4f),
                    string.IsNullOrEmpty(r.GuildName) ? "<color=grey>-</color>" : r.GuildName);
                DialogLayout.DrawCenteredLabel(new Rect(cols[2], ly + 2f, cols[3] - cols[2], rowH - 4f), r.EconomyScore.ToString());
                DialogLayout.DrawCenteredLabel(new Rect(cols[3], ly + 2f, cols[4] - cols[3], rowH - 4f), $"{SilverFmt.Format(r.SilverDonated)}");
                DialogLayout.DrawCenteredLabel(new Rect(cols[4], ly + 2f, cols[5] - cols[4], rowH - 4f), $"{r.SalesEarned}s");
                DialogLayout.DrawCenteredLabel(new Rect(cols[5], ly + 2f, cols[6] - cols[5], rowH - 4f), $"{r.QuestsCompleted}/{r.QuestsPosted}");
                DialogLayout.DrawCenteredLabel(new Rect(cols[6], ly + 2f, cols[7] - cols[6], rowH - 4f), r.SitesBuilt.ToString());
                DialogLayout.DrawCenteredLabel(new Rect(cols[7], ly + 2f, cols[8] - cols[7], rowH - 4f), r.WorkerXp.ToString());
                DialogLayout.DrawCenteredLabel(new Rect(cols[8], ly + 2f, viewRect.width - cols[8], rowH - 4f), FormatTenure(r.FirstSeenUtcTicks));

                ly += rowH;
            }
            if (rows.Count == 0)
            {
                // Differentiate empty-data from filtered-to-empty so the 'why is this empty?' answer is obvious
                bool noData = PlayerStatsCache.Entries == null || PlayerStatsCache.Entries.Count == 0;
                string msg = noData
                    ? "<color=grey>No players to show yet - server hasn't sent a snapshot.</color>"
                    : "<color=grey>No players match the filter.</color>";
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f), msg);
            }

            Widgets.EndScrollView();
        }

        private static float[] ColumnXs(float w)
        {
            // 9 cols: Player | Guild | Score | Donated | Sales | Quests | Sites | XP | Tenure.
            return new float[]
            {
                10f,           // 0 Player
                w * 0.28f,     // 1 Guild
                w * 0.42f,     // 2 Score
                w * 0.52f,     // 3 Donated
                w * 0.62f,     // 4 Sales
                w * 0.72f,     // 5 Quests
                w * 0.80f,     // 6 Sites
                w * 0.86f,     // 7 XP
                w * 0.92f      // 8 Tenure
            };
        }

        private static string FormatTenure(long firstSeenUtcTicks)
        {
            if (firstSeenUtcTicks <= 0) return "<color=grey>-</color>";
            try
            {
                TimeSpan span = DateTime.UtcNow - new DateTime(firstSeenUtcTicks, DateTimeKind.Utc);
                if (span.TotalDays  >= 1) return $"{(int)span.TotalDays}d";
                if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h";
                return $"{Math.Max(1, (int)span.TotalMinutes)}m";
            }
            catch { return "-"; }
        }
    }
}
