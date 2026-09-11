using System;
using System.Collections.Generic;
using System.Linq;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Keeps assigned site workers honest; reports fire only when presence changes, so idle play adds no packets.
    public class GameComponent_KMHSiteWorkerSync : GameComponent
    {
        private const int   IntervalTicks   = 2500;  // ~40s at 1x
        private const float XpPerSyncAtSite = 90f;   // real skill XP per tick while inside the site (Learn saturation applies)

        private int _next;
        private readonly Dictionary<int, bool> _lastPresent  = new Dictionary<int, bool>();
        private readonly HashSet<int>          _deadNotified = new HashSet<int>();

        public GameComponent_KMHSiteWorkerSync(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame < _next) return;
            _next = Find.TickManager.TicksGame + IntervalTicks;
            try { Sync(); }
            catch (Exception ex) { KmhLog.Warn($"Site worker sync threw: {ex.Message}"); }
        }

        private void Sync()
        {
            if (!KmhDispatcher.IsKmhServer || !SiteCache.HasSnapshot) return;
            List<SiteEntry> sites = SiteCache.Snapshot?.Sites;
            string me = KmhSession.Me;
            if (sites == null || string.IsNullOrEmpty(me)) return;

            WorldComponent_KMHSiteWorkers holding = WorldComponent_KMHSiteWorkers.Instance;

            // Index the snapshot: which sites exist, and which of them list me as a worker.
            Dictionary<int, SiteEntry> byTile = new Dictionary<int, SiteEntry>();
            HashSet<int> myWorkerTiles = new HashSet<int>();
            foreach (SiteEntry s in sites)
            {
                if (s == null || s.Tile < 0) continue;
                byTile[s.Tile] = s;
                if (s.Workers != null && s.Workers.Contains(me, StringComparer.OrdinalIgnoreCase)) myWorkerTiles.Add(s.Tile);
            }

            // Must run before reconciliation: a load can lose the record->pawn cross-ref and the Recall button with it.
            holding?.AdoptOrphansFromSnapshot(sites, me);

            // Recalls anything orphaned, so no pawn can stay stuck inside a site that is gone.
            holding?.SyncClaims(
                (tile, user) => byTile.ContainsKey(tile),
                (tile, user) => myWorkerTiles.Contains(tile));

            foreach (SiteEntry s in sites)
            {
                if (s == null || s.Tile < 0) continue;

                // present=true is re-reported even with no server claim, which re-establishes the assignment after a reload.
                if (holding?.IsHoldingAtTile(s.Tile) == true)
                {
                    Pawn heldPawn = holding.HeldPawnAtTile(s.Tile);
                    if (heldPawn != null) GrantSiteXp(heldPawn, s.RelevantSkillDef);
                    _deadNotified.Remove(s.Tile);
                    WorkerProgressDto whp = null;
                    s.WorkerProgress?.TryGetValue(me, out whp);
                    ReportPresence(s, me, heldPawn, whp, present: true);
                    continue;
                }

                // Tag-only fallback: needs a server claim with a real pawn parked at the tile.
                if (s.WorkerProgress == null || !s.WorkerProgress.TryGetValue(me, out WorkerProgressDto wp) || wp == null) continue;
                if (wp.PawnLoadId <= 0) continue;
                Pawn pawn = FindColonist(wp.PawnLoadId);
                if (pawn == null || pawn.Dead)
                {
                    if (_deadNotified.Add(s.Tile))
                    {
                        KmhNotifications.Negative($"{(string.IsNullOrEmpty(wp.PawnName) ? "Your site worker" : wp.PawnName)} is dead or missing - recalled from the site at tile {s.Tile}.");
                        SiteHandler.TryLeave(s.Tile);
                        _lastPresent.Remove(s.Tile);
                    }
                    continue;
                }
                _deadNotified.Remove(s.Tile);
                bool present = IsPawnAtSite(pawn, s.Tile);
                if (present) GrantSiteXp(pawn, s.RelevantSkillDef);
                ReportPresence(s, me, pawn, wp, present);
            }
        }

        // Send a SiteJoin refresh only when presence flips, so re-validations don't spam packets.
        private void ReportPresence(SiteEntry s, string me, Pawn pawn, WorkerProgressDto wp, bool present)
        {
            bool serverPaused = wp != null && !string.IsNullOrEmpty(wp.BlockedReason);
            bool mismatch = serverPaused == present;   // present but server paused, or absent but server active
            if (_lastPresent.TryGetValue(s.Tile, out bool was) && was == present && !mismatch) return;
            _lastPresent[s.Tile] = present;
            int lvl = SkillLevelOf(pawn, s.RelevantSkillDef);
            string name = pawn?.Name?.ToStringShort ?? pawn?.LabelShortCap ?? (wp?.PawnName ?? "");
            int loadId = pawn?.thingIDNumber ?? (wp?.PawnLoadId ?? -1);
            SiteHandler.TryJoin(s.Tile, lvl, name, loadId, present);
        }

        private static bool IsPawnAtSite(Pawn pawn, int tile)
        {
            Caravan car = pawn.GetCaravan();
            return car != null && car.IsPlayerControlled && car.Tile == tile;
        }

        private static Pawn FindColonist(int loadId)
        {
            try
            {
                foreach (Caravan car in Find.WorldObjects.Caravans)
                {
                    if (car == null || !car.IsPlayerControlled) continue;
                    foreach (Pawn p in car.PawnsListForReading)
                        if (p != null && p.thingIDNumber == loadId) return p;
                }
                foreach (Map map in Find.Maps)
                    foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                        if (p != null && p.thingIDNumber == loadId) return p;
            }
            catch { }
            return null;
        }

        private static void GrantSiteXp(Pawn pawn, string skillDefName)
        {
            SkillDef def = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
            SkillRecord rec = def != null ? pawn.skills?.GetSkill(def) : null;
            if (rec != null && !rec.TotallyDisabled) rec.Learn(XpPerSyncAtSite);
        }

        private static int SkillLevelOf(Pawn pawn, string skillDefName)
        {
            SkillDef def = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
            return def != null ? (pawn?.skills?.GetSkill(def)?.Level ?? 0) : 0;
        }
    }
}
