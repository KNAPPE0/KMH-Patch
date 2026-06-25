using System;
using System.Collections.Generic;
using System.Linq;
using GameClient.Misc;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Browse + manage custom sites. Owners can set the reward destination or remove a site; eligible players can
    // join a site as a worker (the client reports their best relevant-skill level) or leave. Production stats come
    // straight from the server snapshot
    public class Dialog_KMHSites : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(820f, 600f);

        private Vector2 _scroll;
        private float   _refresh = DialogLayout.AutoRefreshSeconds;

        public Dialog_KMHSites()
        {
            doCloseX = true; absorbInputAroundWindow = true; draggable = true; resizeable = true;
            SiteHandler.RequestSnapshot();
            SiteCache.Updated += OnUpdated;
        }

        public override void PostClose() { base.PostClose(); SiteCache.Updated -= OnUpdated; }
        private void OnUpdated() { }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Custom sites");

            // Toolbar.
            const float tbW = 150f;
            if (IconButton.Draw(new Rect(rect.width - tbW, y, tbW - 4f, 28f), KMHTextures.Post, "Build site here"))
                Find.WindowStack.Add(new Dialog_KMHBuildSite());
            if (Widgets.ButtonText(new Rect(rect.width - tbW * 2f, y, tbW - 4f, 28f), "Refresh"))
                SiteHandler.RequestSnapshot();
            y += 34f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            SiteSnapshot snap = SiteCache.Snapshot;
            if (snap == null) { DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f), "Loading sites…"); return; }
            if (!snap.AllowCustomSites)
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), "<color=#ffce4d>Custom sites are disabled on this server.</color>");

            if (snap.Sites.Count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 24f, rect.width, 24f), "No sites visible to you yet. Build one with the button above.");
                AutoRefresh();
                return;
            }

            string mine = SessionHandler.Username ?? "";
            float rowH = 78f;
            Rect view = new Rect(0f, y, rect.width, rect.height - y - 6f);
            Rect content = new Rect(0f, 0f, view.width - 16f, snap.Sites.Count * rowH);
            Widgets.BeginScrollView(view, ref _scroll, content);
            DialogLayout.VisibleRange(_scroll, view.height, rowH, snap.Sites.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                DrawRow(new Rect(0f, i * rowH, content.width, rowH - 4f), snap.Sites[i], mine);
            }
            Widgets.EndScrollView();
            AutoRefresh();
        }

        private void DrawRow(Rect r, SiteEntry s, string mine)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(8f);

            bool isOwner  = string.Equals(s.OwnerUsername, mine, StringComparison.OrdinalIgnoreCase);
            bool isWorker = s.Workers.Contains(mine, StringComparer.OrdinalIgnoreCase);

            string owner = LinkedAccountsCache.Format(s.OwnerUsername);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width - 220f, 20f),
                $"<b>{ItemLabels.ResolveLabel(s.ItemDefName)} site</b> <color=grey>· tile {s.Tile} · by</color> {owner}  {AccessTag(s.AccessMode)}");

            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 20f, inner.width - 220f, 18f),
                $"<color=#cccccc>{s.BaseAmountPerCycle}/cycle × {s.ProductionMultiplier:0.00} · ~{s.EffectiveCycleMinutes:0} min/cycle · {s.Workers.Count}/{s.MaxWorkers} workers</color>");

            string roleLine = isOwner ? "<color=#80ff80>You own this</color>"
                : isWorker ? $"<color=#79b8ff>You work here (lvl {WorkerLevel(s, mine)}, {DestLabel(WorkerDest(s, mine))})</color>"
                : "";
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 38f, inner.width - 220f, 18f), roleLine);

            // Right-edge action buttons.
            float bx = inner.xMax - 100f;
            Rect top = new Rect(bx, inner.y, 100f, 24f);
            Rect bot = new Rect(bx, inner.y + 28f, 100f, 24f);

            if (isOwner)
            {
                if (IconButton.Draw(top, KMHTextures.Treasury, "Reward to…")) ShowDestMenu(s.Tile);
                if (IconButton.Draw(bot, KMHTextures.Cancel, "Remove")) SiteHandler.TryCancel(s.Tile);
            }
            else if (isWorker)
            {
                if (IconButton.Draw(top, KMHTextures.Treasury, "Reward to…")) ShowDestMenu(s.Tile);
                if (IconButton.Draw(bot, KMHTextures.Cancel, "Leave")) SiteHandler.TryLeave(s.Tile);
            }
            else if (s.AccessMode != SiteEntry.AccessPrivate && s.Workers.Count < s.MaxWorkers)
            {
                if (IconButton.Draw(top, KMHTextures.Claim, "Join"))
                    SiteHandler.TryJoin(s.Tile, BestColonistSkill(s.RelevantSkillDef));
            }
        }

        private static void ShowDestMenu(int tile)
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Treasury", () => SiteHandler.TrySetDestination(tile, SiteEntry.DestTreasury)),
                new FloatMenuOption("Colony (delivered in-game)", () => SiteHandler.TrySetDestination(tile, SiteEntry.DestCaravan)),
                new FloatMenuOption("Marketplace (auto-list)", () => SiteHandler.TrySetDestination(tile, SiteEntry.DestMarketplace)),
            }));
        }

        private void AutoRefresh()
        {
            _refresh -= Time.deltaTime;
            if (_refresh <= 0f) { _refresh = DialogLayout.AutoRefreshSeconds; SiteHandler.RequestSnapshot(); }
        }

        private static int WorkerLevel(SiteEntry s, string user)
            => s.WorkerProgress.TryGetValue(user, out WorkerProgressDto wp) ? wp.CurrentLevel : 0;
        private static string WorkerDest(SiteEntry s, string user)
            => s.WorkerProgress.TryGetValue(user, out WorkerProgressDto wp) ? wp.Destination : SiteEntry.DestTreasury;

        private static string AccessTag(string a)
            => a == SiteEntry.AccessPublic ? "<color=#80ff80>[public]</color>"
             : a == SiteEntry.AccessPrivate ? "<color=grey>[private]</color>"
             : "<color=#ffce4d>[guild]</color>";
        private static string DestLabel(string d)
            => d == SiteEntry.DestMarketplace ? "→ marketplace"
             : d == SiteEntry.DestCaravan ? "→ colony"
             : "→ treasury";

        // Best relevant-skill level among the player's free colonists (0..20), reported on join so the server
        // doesn't have to trust an asserted skill
        private static int BestColonistSkill(string skillDefName)
        {
            SkillDef def = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName ?? "Crafting") ?? SkillDefOf.Crafting;
            int best = 0;
            try
            {
                List<Map> maps = Find.Maps;
                if (maps != null)
                    foreach (Map m in maps)
                        foreach (Pawn p in m.mapPawns.FreeColonists)
                        {
                            int lvl = p.skills?.GetSkill(def)?.Level ?? 0;
                            if (lvl > best) best = lvl;
                        }
            }
            catch { }
            return Mathf.Clamp(best, 0, 20);
        }
    }
}
