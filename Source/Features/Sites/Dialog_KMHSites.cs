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
            else
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f), "<color=grey>Assign a colonist from a caravan on the site tile: they enter the site (away from home, not usable) and earn real skill XP. Recall brings them back to a caravan at the tile.</color>");
            y += 22f;

            if (snap.Sites.Count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 24f, rect.width, 24f), "No sites visible to you yet. Build one with the button above.");
                AutoRefresh();
                return;
            }

            string mine = KmhSession.Me;
            float rowH = 96f;   // room for the worker-roster line
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

            bool isOwner  = KmhSession.Same(s.OwnerUsername, mine);
            // Worker state, priority order:
            //  - heldHere: I hold a pawn inside this site (tile-authoritative - survives reload / empty snapshot /
            //    username mismatch on reconnect).
            //  - wpHasPawn: server lists me with a real pawn (assign-but-not-yet-saved; pawn still in a caravan).
            //  - serverListsMe: server has ANY claim for me (even legacy/pawnless) - still show Recall/Clear so a
            //    stuck claim is never hidden behind a bare "Assign".
            bool heldHere      = WorkerHolding?.IsHoldingAtTile(s.Tile) == true;
            WorkerProgressDto myWp = null;
            s.WorkerProgress?.TryGetValue(mine, out myWp);
            bool wpHasPawn     = myWp != null && myWp.PawnLoadId > 0 && !myWp.Legacy;
            bool serverListsMe = s.Workers != null && s.Workers.Contains(mine, StringComparer.OrdinalIgnoreCase);
            bool hasPawnLink   = heldHere || wpHasPawn;                 // a real pawn is (or should be) inside
            bool isWorker      = hasPawnLink || serverListsMe;          // any claim of mine → show Recall/Clear

            string owner = LinkedAccountsCache.Format(s.OwnerUsername);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width - 220f, 20f),
                $"<b>{ItemLabels.ResolveLabel(s.ItemDefName)} site</b> <color=grey>· T{s.OutputTier} · tile {s.Tile} · by</color> {owner}  {AccessTag(s.AccessMode)}");

            // Paused sites (no workers) never show a bogus cycle time - clear "Paused" text + the caravan->assign hint.
            string prodLine = !s.IsProducing
                ? $"<color=#ffcf59>Paused, no workers</color> <color=grey>· {s.BaseAmountPerCycle}/cycle · {s.Workers.Count}/{s.MaxWorkers} workers · send a caravan to tile {s.Tile} and assign a colonist</color>"
                : $"<color=#cccccc>{s.BaseAmountPerCycle}/cycle × {s.ProductionMultiplier:0.00} · ~{s.EffectiveCycleMinutes:0} min/cycle · {s.Workers.Count}/{s.MaxWorkers} workers</color>";
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 20f, inner.width - 220f, 18f), prodLine);

            string myPawn = WorkerPawn(s, mine);
            if (string.IsNullOrEmpty(myPawn) && heldHere) myPawn = WorkerHolding.HeldPawnAtTile(s.Tile)?.Name?.ToStringShort ?? "";
            string who = string.IsNullOrEmpty(myPawn) ? "Your colonist" : myPawn;
            bool paused = myWp != null && !string.IsNullOrEmpty(myWp.BlockedReason);
            // A valid off-map worker reads as "working"/"away at this site"; a claim with no recoverable pawn asks
            // for cleanup instead of pretending someone is there.
            string roleLine;
            if (hasPawnLink)
                roleLine = paused
                    ? $"<color=#ffcf59>{who} is away at this site (paused) · {DestLabel(WorkerDest(s, mine))}</color>"
                    : $"<color=#79b8ff>{who} is working at this site (away from home) · lvl {WorkerLevel(s, mine)} · {DestLabel(WorkerDest(s, mine))}</color>";
            else if (serverListsMe)
                roleLine = "<color=#ffcf59>Worker claim needs cleanup - no pawn is here. Use Clear claim.</color>";
            else
                roleLine = isOwner ? "<color=#80ff80>You own this</color>" : "";
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 38f, inner.width - 220f, 18f), roleLine);

            // Worker roster by pawn name (falls back to the account name for legacy workers).
            string roster = WorkerRoster(s);
            if (!string.IsNullOrEmpty(roster))
                DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 56f, inner.width - 110f, 16f),
                    $"<color=grey>Workers: {roster}</color>");

            // Right-edge action buttons: up to three stacked slots (assign/recall · reward · remove-site).
            float bx = inner.xMax - 100f;
            Rect r0 = new Rect(bx, inner.y, 100f, 22f);
            Rect r1 = new Rect(bx, inner.y + 26f, 100f, 22f);
            Rect r2 = new Rect(bx, inner.y + 52f, 100f, 22f);

            if (isWorker)
            {
                // A real pawn inside → Recall it; a stuck/pawnless claim → Clear it. Both drop the server claim; only
                // Recall also returns a held pawn. Reward destination + owner Remove stay available.
                string label = hasPawnLink ? "Recall pawn" : "Clear claim";
                if (IconButton.Draw(r0, KMHTextures.Cancel, label)) RecallWorker(s);
                if (IconButton.Draw(r1, KMHTextures.Treasury, "Reward to…")) ShowDestMenu(s.Tile);
                if (isOwner && IconButton.Draw(r2, KMHTextures.Cancel, "Remove site")) ConfirmRemoveSite(s.Tile);
            }
            else if (isOwner)
            {
                if (IconButton.Draw(r0, KMHTextures.Claim, "Assign pawn")) PickWorkerPawn(s);
                if (IconButton.Draw(r1, KMHTextures.Cancel, "Remove site")) ConfirmRemoveSite(s.Tile);
            }
            else if (s.AccessMode != SiteEntry.AccessPrivate && s.Workers.Count < s.MaxWorkers)
            {
                if (IconButton.Draw(r0, KMHTextures.Claim, "Assign pawn")) PickWorkerPawn(s);
            }
        }

        private static WorldComponent_KMHSiteWorkers WorkerHolding => WorldComponent_KMHSiteWorkers.Instance;

        private static void ConfirmRemoveSite(int tile)
            => Find.WindowStack.Add(Verse.Dialog_MessageBox.CreateConfirmation(
                "Remove this site? Any colonist working inside is recalled to a caravan at the site tile first.",
                () => { RecallMineAt(tile); SiteHandler.TryCancel(tile); }));

        // Recall my held pawn at a tile (if any) - shared by the Recall button and site removal. Tile-based so it
        // works even if the record's stored username no longer matches the session (reconnect edge case).
        private static void RecallMineAt(int tile)
        {
            if (WorkerHolding?.IsHoldingAtTile(tile) == true && WorkerHolding.RecallAtTile(tile, out string where))
                Notifications.KmhNotifications.Positive($"Your colonist returned to {where}.");
        }

        private static void RecallWorker(SiteEntry s)
        {
            RecallMineAt(s.Tile);
            SiteHandler.TryLeave(s.Tile);   // tell the server to drop the worker claim
        }

        // Assign a real colonist that has reached the site tile by caravan. No account-level fallback - a site needs a
        // real pawn, so with no caravan present we tell the player to send one.
        private static void PickWorkerPawn(SiteEntry s)
        {
            List<Pawn> pawns = PawnsAtSiteTile(s.Tile);
            if (pawns.Count == 0)
            {
                Notifications.KmhNotifications.Rejected($"Send a caravan to this site's tile (world tile {s.Tile}), then assign a colonist.");
                return;
            }
            SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(s.RelevantSkillDef);
            List<FloatMenuOption> opts = new List<FloatMenuOption>();
            foreach (Pawn p in pawns)
            {
                Pawn pawn = p;
                int lvl = skill != null ? (pawn.skills?.GetSkill(skill)?.Level ?? 0) : 0;
                string name = pawn.Name?.ToStringShort ?? pawn.LabelShortCap;
                opts.Add(new FloatMenuOption($"{name}  ({s.RelevantSkillDef} {lvl})", () => AssignWorker(s, pawn, lvl, name)));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Pull the pawn INTO the site (out of its caravan, unusable at home) then tell the server it's assigned +
        // present. If holding fails for any reason, fall back to the tag-only assignment so the worker still counts.
        private static void AssignWorker(SiteEntry s, Pawn pawn, int level, string name)
        {
            string mine = KmhSession.Me;
            bool held = WorkerHolding?.Hold(pawn, s.Tile, mine) == true;
            SiteHandler.TryJoin(s.Tile, level, name, pawn.thingIDNumber, present: true);
            Notifications.KmhNotifications.Positive(held
                ? $"{name} entered the site and is now working there (recall from this menu to bring them home)."
                : $"{name} assigned - keep a caravan on tile {s.Tile} so they keep working (couldn't move them inside).");
        }

        // Colonists in a player caravan sitting on the site's world tile (physical caravan-arrival requirement).
        private static List<Pawn> PawnsAtSiteTile(int tile)
        {
            List<Pawn> outList = new List<Pawn>();
            try
            {
                foreach (RimWorld.Planet.Caravan car in Find.WorldObjects.Caravans)
                {
                    if (car == null || !car.IsPlayerControlled || car.Tile != tile) continue;
                    foreach (Pawn p in car.PawnsListForReading)
                        if (p?.IsColonist == true && !p.Dead) outList.Add(p);
                }
            }
            catch { }
            return outList;
        }

        // Pawn name for a given worker username (empty if legacy/none).
        private static string WorkerPawn(SiteEntry s, string user)
        {
            if (s?.WorkerProgress == null || string.IsNullOrEmpty(user)) return "";
            return s.WorkerProgress.TryGetValue(user, out WorkerProgressDto wp) && wp != null ? (wp.PawnName ?? "") : "";
        }

        // Worker names by pawn; legacy/blocked account-workers show disabled + reason (server-reported).
        private static string WorkerRoster(SiteEntry s)
        {
            if (s?.Workers == null || s.Workers.Count == 0) return "";
            List<string> names = new List<string>();
            foreach (string u in s.Workers)
            {
                s.WorkerProgress.TryGetValue(u, out WorkerProgressDto wp);
                if (wp != null && (wp.Legacy || !string.IsNullOrEmpty(wp.BlockedReason)))
                {
                    string why = wp.Legacy ? "legacy - reassign a pawn" : wp.BlockedReason;
                    names.Add($"<color=#ffcf59>{LinkedAccountsCache.Format(u)} (disabled: {why})</color>");
                    continue;
                }
                string pawn = WorkerPawn(s, u);
                names.Add(string.IsNullOrEmpty(pawn) ? LinkedAccountsCache.Format(u) : $"{pawn} <color=grey>({LinkedAccountsCache.Format(u)})</color>");
            }
            return string.Join(", ", names);
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
            => s?.WorkerProgress != null && s.WorkerProgress.TryGetValue(user, out WorkerProgressDto wp) ? wp.CurrentLevel : 0;
        private static string WorkerDest(SiteEntry s, string user)
            => s?.WorkerProgress != null && s.WorkerProgress.TryGetValue(user, out WorkerProgressDto wp) ? wp.Destination : SiteEntry.DestTreasury;

        private static string AccessTag(string a)
            => a == SiteEntry.AccessPublic ? "<color=#80ff80>[public]</color>"
             : a == SiteEntry.AccessPrivate ? "<color=grey>[private]</color>"
             : "<color=#ffce4d>[guild]</color>";
        private static string DestLabel(string d)
            => d == SiteEntry.DestMarketplace ? "→ marketplace"
             : d == SiteEntry.DestCaravan ? "→ colony"
             : "→ treasury";
    }
}
