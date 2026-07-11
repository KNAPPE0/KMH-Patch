using System;
using System.Collections.Generic;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Guilds
{
    // Join picker: every guild (from the leaderboard snapshot) - open ones join in one click, invite-only tagged.
    public class Dialog_KMHGuildPicker : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(480f, 560f);

        private string  _search = "";
        private Vector2 _scroll;
        private float   _refreshTimer;

        public Dialog_KMHGuildPicker()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;

            GuildHandler.RequestLeaderboard();
            _refreshTimer = DialogLayout.AutoRefreshSeconds;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                GuildHandler.RequestLeaderboard();
            }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Join a guild");

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                "<color=grey>Open guilds join instantly. Invite-only guilds need an invite from their officers.</color>");
            y += 24f;

            _search = Widgets.TextField(new Rect(0f, y, rect.width, 28f), _search ?? "");
            y += 34f;

            List<GuildLeaderboardEntry> all = GuildLeaderboardCache.Snapshot?.Guilds ?? new List<GuildLeaderboardEntry>();
            List<GuildLeaderboardEntry> shown = new List<GuildLeaderboardEntry>();
            string q = (_search ?? "").Trim();
            foreach (GuildLeaderboardEntry g in all)
                if (q.Length == 0 || g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    shown.Add(g);
            // Open (joinable) guilds first, then by size.
            shown.Sort((a, b) => a.OpenJoin != b.OpenJoin ? (a.OpenJoin ? -1 : 1) : b.MemberCount.CompareTo(a.MemberCount));

            Rect view = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            float rowH = 34f;
            Rect inner = new Rect(0f, 0f, view.width - 16f, Math.Max(shown.Count * rowH, view.height));
            Widgets.BeginScrollView(view, ref _scroll, inner);
            float yy = 0f;
            foreach (GuildLeaderboardEntry g in shown)
            {
                Rect row = new Rect(0f, yy, inner.width, rowH - 2f);
                if (yy % (rowH * 2) < rowH) Widgets.DrawLightHighlight(row);

                string tag = g.OpenJoin ? "<color=#7CD37C>open</color>" : "<color=grey>invite-only</color>";
                DialogLayout.LabelTrunc(new Rect(6f, yy + 6f, inner.width - 100f, 22f),
                    $"<b>{g.Name}</b>  <color=grey>({g.MemberCount} member{(g.MemberCount == 1 ? "" : "s")})</color>  {tag}");
                if (g.OpenJoin)
                {
                    if (Widgets.ButtonText(new Rect(inner.width - 84f, yy + 3f, 80f, 26f), "Join"))
                    {
                        GuildHandler.TryJoin(g.Name);
                        Close();
                    }
                }
                yy += rowH;
            }
            if (shown.Count == 0)
                DialogLayout.LabelTrunc(new Rect(6f, 4f, inner.width - 12f, 22f),
                    all.Count == 0 ? "<color=grey>Loading guilds…</color>"
                                   : "<color=grey>No guild matches that search.</color>");
            Widgets.EndScrollView();

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }
    }
}
