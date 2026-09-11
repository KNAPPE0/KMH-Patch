using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.PlayerStats;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Mail
{
    // Lists every ranked player, online or not, so an offline recipient can still be chosen.
    public class Dialog_KMHPlayerPicker : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(460f, 520f);

        private readonly Action<string> _onPick;
        private readonly string         _title;
        private readonly KmhFilteredView<string> _view = new KmhFilteredView<string>();
        private Vector2 _scroll;
        private string  _filter = "";

        private Dialog_KMHPlayerPicker(string title, Action<string> onPick)
        {
            _title  = title;
            _onPick = onPick;
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
        }

        public static void Open(string title, Action<string> onPick)
        {
            try { PlayerStatsHandler.RequestSnapshot(); } catch { }   // freshen the roster on open
            Find.WindowStack.Add(new Dialog_KMHPlayerPicker(title, onPick));
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, _title);
            DialogLayout.DrawSectionDivider(rect, ref y);

            _filter = DialogLayout.SearchField(new Rect(0f, y, rect.width, 28f), _filter, "Search players");
            y += 34f;

            List<PlayerStats.Dto.PlayerLeaderboardEntry> src = Features.PlayerStats.PlayerStatsCache.Entries;
            string flt = (_filter ?? "").Trim().ToLowerInvariant();
            List<string> players = _view.Get(src, flt, () => Build(src, flt));

            Rect listBox = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(listBox);
            string empty = Features.PlayerStats.PlayerStatsCache.Entries == null
                ? "<color=grey>Loading players…</color>"
                : (players.Count == 0 ? "<color=grey>No players match.</color>" : null);
            DialogLayout.ScrollList(listBox, ref _scroll, players.Count, 30f, (i, row) => DrawRow(row, players[i]), empty);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void DrawRow(Rect row, string username)
        {
            Rect inner = row.ContractedBy(4f);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width, inner.height), LinkedAccountsCache.Format(username), TextAnchor.MiddleLeft);
            if (Widgets.ButtonInvisible(row))
            {
                _onPick?.Invoke(username);
                Close();
            }
        }

        // Distinct roster usernames minus yourself, filtered by username or linked Discord name, sorted alphabetically.
        private static List<string> Build(List<PlayerStats.Dto.PlayerLeaderboardEntry> entries, string flt)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outList = new List<string>();
            string me = KmhSession.Me;

            if (entries != null)
                foreach (var e in entries)
                {
                    string u = e?.Username;
                    if (string.IsNullOrEmpty(u) || KmhSession.Same(u, me) || !seen.Add(u)) continue;
                    if (flt.Length > 0)
                    {
                        string disc = (LinkedAccountsCache.DiscordNameFor(u) ?? "").ToLowerInvariant();
                        if (!u.ToLowerInvariant().Contains(flt) && !disc.Contains(flt)) continue;
                    }
                    outList.Add(u);
                }

            outList.Sort(StringComparer.OrdinalIgnoreCase);
            return outList;
        }
    }
}
