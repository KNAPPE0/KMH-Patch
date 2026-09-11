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
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(480f, 560f);

        private string  _search = "";
        private readonly UI.KmhFilteredView<GuildLeaderboardEntry> _view = new UI.KmhFilteredView<GuildLeaderboardEntry>();
        private Vector2 _scroll;

        public Dialog_KMHGuildPicker()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;

            EnableAutoRefresh(() => GuildHandler.RequestLeaderboard());
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Join a guild");

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                // Never promises "instantly": an owner can require proximity to the hall, and that rule is not on the snapshot.
                "<color=grey>Open guilds take you without an invite; invite-only guilds need one from their officers. " +
                "Some servers also require you to be near the guild's hall.</color>");
            y += 24f;

            _search = Widgets.TextField(new Rect(0f, y, rect.width, 28f), _search ?? "");
            y += 34f;

            List<GuildLeaderboardEntry> all = GuildLeaderboardCache.Snapshot?.Guilds;
            string q = (_search ?? "").Trim();
            List<GuildLeaderboardEntry> shown = _view.Get(all, q, () =>
            {
                List<GuildLeaderboardEntry> outList = new List<GuildLeaderboardEntry>();
                if (all != null)
                    foreach (GuildLeaderboardEntry g in all)
                        if (q.Length == 0 || g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            outList.Add(g);
                // Open (joinable) guilds first, then by size.
                outList.Sort((a, b) => a.OpenJoin != b.OpenJoin ? (a.OpenJoin ? -1 : 1) : b.MemberCount.CompareTo(a.MemberCount));
                return outList;
            });

            Rect view = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
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
