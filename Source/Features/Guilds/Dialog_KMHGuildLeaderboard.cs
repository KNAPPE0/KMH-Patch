using System;
using System.Collections.Generic;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Guilds
{
    // Cross-guild leaderboard view. Pulls every guild on the server from GuildLeaderboardCache and renders a
    // sortable table, parallel to Dialog_KMHPlayerLeaderboard so the two feel like siblings.
    //
    // Surfaces the metrics the server tracks today (member count + current treasury silver); more land as their
    // tracking arrives.
    public class Dialog_KMHGuildLeaderboard : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(720f, 540f);

        private Vector2  _scroll          = Vector2.zero;
        private float    _refreshTimer    = DialogLayout.AutoRefreshSeconds;
        private DateTime _lastRefreshUtc  = DateTime.UtcNow;
        private SortMode _sort            = SortMode.MemberCount;

        // Cached filtered+sorted view so we don't re-sort every redraw. Invalidated on snapshot arrival OR sort-key
        // change
        private List<GuildLeaderboardEntry> _visible;
        private SortMode                    _visibleSort;
        private GuildLeaderboardSnapshot    _visibleSource;

        private enum SortMode
        {
            MemberCount,
            TreasurySilver,
            Name,
        }

        public Dialog_KMHGuildLeaderboard()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            // Fresh request on open so opening the dialog feels live.
            GuildHandler.RequestLeaderboard();
            _lastRefreshUtc = DateTime.UtcNow;

            // React to server-pushed snapshots - keeps the "live" badge accurate and invalidates the sorted cache
            GuildLeaderboardCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            GuildLeaderboardCache.Updated -= OnSnapshotUpdated;
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
                GuildHandler.RequestLeaderboard();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Guild Leaderboard");

            int secsSince = Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds);
            DialogLayout.DrawLiveBadge(rect, secsSince);
            DialogLayout.DrawSectionDivider(rect, ref y);

            // Toolbar - sort dropdown + refresh.
            if (Widgets.ButtonText(new Rect(0f, y, 220f, 28f), $"Sort: {DialogLayout.FriendlyEnumName(_sort)}"))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                foreach (SortMode m in Enum.GetValues(typeof(SortMode)))
                {
                    SortMode captured = m;
                    opts.Add(new FloatMenuOption(DialogLayout.FriendlyEnumName(captured),
                        () => { _sort = captured; _visible = null; }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            if (Widgets.ButtonText(new Rect(rect.width - 110f, y, 100f, 28f), "Refresh"))
            {
                GuildHandler.RequestLeaderboard();
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
            DialogLayout.LabelTrunc(new Rect(cols[0], r.y, cols[1] - cols[0], r.height),              "<b>#</b>");
            DialogLayout.LabelTrunc(new Rect(cols[1], r.y, cols[2] - cols[1], r.height),              "<b>Guild</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[2], r.y, cols[3] - cols[2], r.height), "<b>Members</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[3], r.y, r.width   - cols[3], r.height), "<b>Treasury</b>");
        }

        private void DrawRows(Rect box)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = DialogLayout.RowHeightSingle;

            List<GuildLeaderboardEntry> rows = EnsureVisible();
            if (rows.Count == 0)
            {
                string msg = GuildLeaderboardCache.HasSnapshot
                    ? "No guilds exist on this server yet. /kmh guild create <name> in chat to start one."
                    : "Loading…";
                Widgets.Label(inner, $"<color=grey>{msg}</color>");
                return;
            }

            float contentH = rows.Count * rowH;
            Rect view = new Rect(inner.x, inner.y, inner.width - 16f, contentH);
            Widgets.BeginScrollView(inner, ref _scroll, view);

            // Highlight my own guild if I'm in one - same convention as the player leaderboard's "★ me" marker
            string myGuild = GuildCache.HasSnapshot && GuildCache.InGuild
                ? (GuildCache.Guild?.Name ?? "")
                : "";

            float ly = view.y;
            float[] cols = ColumnXs(view.width);
            for (int i = 0; i < rows.Count; i++)
            {
                GuildLeaderboardEntry g = rows[i];
                Rect rowRect = new Rect(view.x, ly, view.width, rowH);
                if (i % 2 == 1) Widgets.DrawLightHighlight(rowRect);

                bool isMine = !string.IsNullOrEmpty(myGuild)
                           && string.Equals(g.Name, myGuild, StringComparison.OrdinalIgnoreCase);
                string nameCell = isMine ? $"★ {g.Name}" : (g.Name ?? "");

                DialogLayout.LabelTrunc(new Rect(cols[0], ly, cols[1] - cols[0], rowH), $"{i + 1}");
                DialogLayout.LabelTrunc(new Rect(cols[1], ly, cols[2] - cols[1], rowH), nameCell);
                DialogLayout.DrawCenteredLabel(new Rect(cols[2], ly, cols[3] - cols[2], rowH),
                                               g.MemberCount.ToString("N0"));
                DialogLayout.DrawCenteredLabel(new Rect(cols[3], ly, view.width - cols[3], rowH),
                                               $"{g.TreasurySilver:N0}s");
                ly += rowH;
            }
            Widgets.EndScrollView();
        }

        // Build the sorted snapshot once per source/sort change. Cheap sort but every redraw runs at 60Hz; avoiding
        // redundant work is worth the few lines of caching
        private List<GuildLeaderboardEntry> EnsureVisible()
        {
            GuildLeaderboardSnapshot snap = GuildLeaderboardCache.Snapshot;
            if (_visible != null && _visibleSort == _sort && ReferenceEquals(_visibleSource, snap))
                return _visible;

            List<GuildLeaderboardEntry> src = snap?.Guilds ?? new List<GuildLeaderboardEntry>();
            List<GuildLeaderboardEntry> result = new List<GuildLeaderboardEntry>(src);
            switch (_sort)
            {
                case SortMode.TreasurySilver:
                    result.Sort((a, b) => b.TreasurySilver.CompareTo(a.TreasurySilver));
                    break;
                case SortMode.Name:
                    result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortMode.MemberCount:
                default:
                    result.Sort((a, b) => b.MemberCount.CompareTo(a.MemberCount));
                    break;
            }

            _visible       = result;
            _visibleSort   = _sort;
            _visibleSource = snap;
            return result;
        }

        // Column x-offsets - narrow first column (rank), wide guild name, numeric columns right-of-center
        private static float[] ColumnXs(float width)
        {
            return new float[]
            {
                10f,            // rank
                50f,            // guild name
                width * 0.55f,  // members
                width * 0.75f,  // treasury
            };
        }
    }
}
