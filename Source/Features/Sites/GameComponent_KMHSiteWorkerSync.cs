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
    // Reports fire only when presence changes, so idle play adds no packets.
    public class GameComponent_KMHSiteWorkerSync : GameComponent
    {
        private const int   IntervalTicks   = 2500;
        private const float XpPerSyncAtSite = 90f;   // RimWorld's Learn saturation still applies

        private int _next;
        // Per colonist, not per site: a per-tile key lets one pawn's presence overwrite another's.
        private readonly Dictionary<(int Tile, int PawnLoadId), bool> _lastPresent  = new Dictionary<(int, int), bool>();
        private readonly HashSet<(int Tile, int PawnLoadId)>          _deadNotified = new HashSet<(int, int)>();

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

            Dictionary<int, SiteEntry> byTile = new Dictionary<int, SiteEntry>();
            HashSet<int> myWorkerTiles = new HashSet<int>();
            foreach (SiteEntry s in sites)
            {
                if (s == null || s.Tile < 0) continue;
                byTile[s.Tile] = s;
                if (SiteWorkerKey.Works(s, me)) myWorkerTiles.Add(s.Tile);
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

                // Re-reported even without a server claim, which re-establishes the assignment after a reload.
                List<Pawn> heldHere = holding?.HeldPawnsFor(s.Tile, me) ?? new List<Pawn>();
                var reported = new HashSet<int>();
                foreach (Pawn heldPawn in heldHere)
                {
                    if (heldPawn == null) continue;
                    GrantSiteXp(heldPawn, s.RelevantSkillDef);
                    _deadNotified.Remove((s.Tile, heldPawn.thingIDNumber));
                    reported.Add(heldPawn.thingIDNumber);
                    ReportPresence(s, me, heldPawn, SiteWorkerKey.For(s, me, heldPawn.thingIDNumber), present: true);
                }

                // Tag-only fallback: needs a server claim with a real pawn parked at the tile.
                foreach (KeyValuePair<string, WorkerProgressDto> kv in SiteWorkerKey.Mine(s, me))
                {
                    WorkerProgressDto wp = kv.Value;
                    if (wp.PawnLoadId <= 0 || reported.Contains(wp.PawnLoadId)) continue;
                    Pawn pawn = FindColonist(wp.PawnLoadId);
                    if (pawn == null || pawn.Dead)
                    {
                        if (_deadNotified.Add((s.Tile, wp.PawnLoadId)))
                        {
                            KmhNotifications.Negative($"{(string.IsNullOrEmpty(wp.PawnName) ? "Your site worker" : wp.PawnName)} is dead or missing - recalled from the site at tile {s.Tile}.");
                            // Names the colonist, so one dead pawn does not pull the whole crew off the site.
                            SiteHandler.TryLeave(s.Tile, wp.PawnLoadId);
                            _lastPresent.Remove((s.Tile, wp.PawnLoadId));
                        }
                        continue;
                    }
                    _deadNotified.Remove((s.Tile, wp.PawnLoadId));
                    bool present = IsPawnAtSite(pawn, s.Tile);
                    if (present) GrantSiteXp(pawn, s.RelevantSkillDef);
                    ReportPresence(s, me, pawn, wp, present);
                }
            }
        }

        private void ReportPresence(SiteEntry s, string me, Pawn pawn, WorkerProgressDto wp, bool present)
        {
            bool serverPaused = wp != null && !string.IsNullOrEmpty(wp.BlockedReason);
            bool mismatch = serverPaused == present;   // present but server paused, or absent but server active
            int loadId = pawn?.thingIDNumber ?? (wp?.PawnLoadId ?? -1);
            var slot = (s.Tile, loadId);
            if (_lastPresent.TryGetValue(slot, out bool was) && was == present && !mismatch) return;
            _lastPresent[slot] = present;
            int lvl = SkillLevelOf(pawn, s.RelevantSkillDef);
            string name = pawn?.Name?.ToStringShort ?? pawn?.LabelShortCap ?? (wp?.PawnName ?? "");
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
