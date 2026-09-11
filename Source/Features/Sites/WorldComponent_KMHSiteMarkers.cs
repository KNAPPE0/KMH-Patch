using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.SubProtocol;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Driven from the world tick AND a per-frame patch, so markers also show on the paused world and landing-site views.
    public class WorldComponent_KMHSiteMarkers : WorldComponent
    {
        private static float _lastReconcileReal = -999f;

        // Tiles the current world doesn't have. Reconcile runs continuously, so warn once each instead of every pass.
        private static readonly HashSet<int> _warnedTiles = new HashSet<int>();

        // A site built on another world names a tile this planet lacks, and resolving it throws deep inside RimWorld.
        private static bool TileExists(int tile)
        {
            if (tile < 0) return false;
            try { return Find.WorldGrid != null && tile < Find.WorldGrid.TilesCount; }
            catch { return false; }
        }

        // Reconcile retries failed tiles every pass; logging per pass once cost a player 218 identical lines.
        private static readonly Dictionary<int, string> _warnedPlacements = new Dictionary<int, string>();

        internal static bool ShouldWarnPlacement(Dictionary<int, string> seen, int tile, string reason)
        {
            if (seen.TryGetValue(tile, out string had) && had == reason) return false;
            seen[tile] = reason;
            return true;
        }

        private static void WarnPlacementOnce(int tile, string reason)
        {
            if (!ShouldWarnPlacement(_warnedPlacements, tile, reason)) return;
            KmhLog.Warn($"Could not place site marker at tile {tile}: {reason}");
        }

        private static void WarnMissingTileOnce(int tile, string what)
        {
            if (!_warnedTiles.Add(tile)) return;
            KmhLog.Warn($"{what} sits on world tile {tile}, which this planet does not have - marker skipped. " +
                        "The entry still works from the KMH tab; it was created on a different world.");
        }

        // RimWorld.Planet.World qualified - the KMHPatch.Features.World namespace would otherwise shadow bare 'World'
        public WorldComponent_KMHSiteMarkers(RimWorld.Planet.World world) : base(world) { }

        public override void WorldComponentTick() => TryReconcile(2f);

        // Throttled by real time, so both drivers can call it every frame.
        internal static void TryReconcile(float minSeconds)
        {
            if (Find.World == null || Find.WorldObjects == null) return;
            float now = Time.realtimeSinceStartup;
            if (now - _lastReconcileReal < minSeconds && now >= _lastReconcileReal) return;   // (guard clock resets too)
            _lastReconcileReal = now;
            try { ReconcileSites(); }      catch (Exception ex) { KmhLog.Warn($"Site markers reconcile threw: {ex.Message}"); }
            try { ReconcileGuildHall(); }  catch (Exception ex) { KmhLog.Warn($"Guild hall marker reconcile threw: {ex.Message}"); }
        }

        // Reused across passes: this runs twice a second for the whole session, and the garbage adds up.
        private static readonly List<KMHSiteWorldObject> _existing = new List<KMHSiteWorldObject>();
        private static readonly Dictionary<int, SiteEntry> _desired = new Dictionary<int, SiteEntry>();
        private static readonly HashSet<int> _have = new HashSet<int>();

        private static void ReconcileSites()
        {
            List<KMHSiteWorldObject> existing = _existing;
            existing.Clear();
            foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
                if (wo is KMHSiteWorldObject m) existing.Add(m);

            // Not connected / no data: drop any leftover markers (covers disconnect and loading a save offline) and stop.
            if (!KmhDispatcher.IsKmhServer || !SiteCache.HasSnapshot || SiteCache.Snapshot?.Sites == null)
            {
                foreach (KMHSiteWorldObject m in existing) Find.WorldObjects.Remove(m);
                existing.Clear();
                return;
            }

            Dictionary<int, SiteEntry> desired = _desired;
            desired.Clear();
            foreach (SiteEntry s in SiteCache.Snapshot.Sites)
            {
                if (s == null || s.Tile < 0) continue;
                if (!TileExists(s.Tile)) { WarnMissingTileOnce(s.Tile, "Site"); continue; }
                desired[s.Tile] = s;
            }

            HashSet<int> have = _have;
            have.Clear();
            foreach (KMHSiteWorldObject m in existing)
            {
                int tile = m.Tile.tileId;
                if (have.Contains(tile)) { Find.WorldObjects.Remove(m); continue; }   // dedupe stray doubles
                // A def is fixed at creation, so a site turning into an outpost must be recreated or it keeps the old art.
                if (desired.TryGetValue(tile, out SiteEntry s) && DefNameFor(s) == m.def?.defName)
                { Apply(m, s); have.Add(tile); }
                else Find.WorldObjects.Remove(m);
            }

            foreach (KeyValuePair<int, SiteEntry> kv in desired)
            {
                if (have.Contains(kv.Key) || kv.Key < 0) continue;
                WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(DefNameFor(kv.Value));
                if (def == null) continue;
                try
                {
                    KmhMarkerArt.ForgetMisses();   // a new marker is a new chance for art that was not loaded yet
                    KMHSiteWorldObject m = (KMHSiteWorldObject)WorldObjectMaker.MakeWorldObject(def);
                    m.Tile = kv.Key;
                    Apply(m, kv.Value);
                    Find.WorldObjects.Add(m);
                    have.Add(kv.Key);
                }
                catch (Exception ex) { WarnPlacementOnce(kv.Key, ex.Message); }
            }

            // Held references would keep removed markers alive until the next pass.
            existing.Clear();
            desired.Clear();
        }

        // Both defs share one worldObjectClass, so the kind lives in the def name alone.
        private static string DefNameFor(SiteEntry s) => KmhMarkerArt.IsOutpost(s) ? "KMHOutpost" : "KMHSite";

        private static void Apply(KMHSiteWorldObject m, SiteEntry s)
        {
            m.Entry          = s;
            m.SiteOwner      = string.IsNullOrEmpty(s.OwnerGuild) ? (s.OwnerUsername ?? "") : $"{s.OwnerGuild} (guild)";
            m.MineToManage   = SiteOwnershipClient.CanManage(s, UI.KmhSession.Me);
            m.SiteItem       = UI.ItemLabels.ResolveLabel(s.ItemDefName);
            m.SiteWorkers    = s.Workers?.Count ?? 0;
            m.SiteMaxWorkers = s.MaxWorkers;
        }

        // Driven by the guild snapshot, not the site one, so a hall marker shows even when the player owns no sites.
        private static readonly List<Guilds.KMHGuildHallWorldObject> _halls = new List<Guilds.KMHGuildHallWorldObject>();

        private static void ReconcileGuildHall()
        {
            List<Guilds.KMHGuildHallWorldObject> existing = _halls;
            existing.Clear();
            foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
                if (wo is Guilds.KMHGuildHallWorldObject h) existing.Add(h);

            var g = Guilds.GuildCache.Guild;
            bool want = KmhDispatcher.IsKmhServer && g?.Hall != null && g.Hall.HasHall && g.Hall.Tile >= 0;
            if (want && !TileExists(g.Hall.Tile)) { WarnMissingTileOnce(g.Hall.Tile, "Guild hall"); want = false; }

            // Remove anything that isn't the wanted hall (disconnect, hall removed/moved, or a stray duplicate).
            bool applied = false;
            foreach (Guilds.KMHGuildHallWorldObject h in existing)
            {
                if (want && !applied && h.Tile.tileId == g.Hall.Tile)
                {
                    h.GuildName = g.Name ?? ""; h.RadiusTiles = g.Hall.RadiusTiles; applied = true;
                }
                else Find.WorldObjects.Remove(h);
            }
            existing.Clear();   // a held reference would keep a removed hall alive until the next pass
            if (!want || applied) return;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("KMHGuildHall");
            if (def == null) return;
            try
            {
                var h = (Guilds.KMHGuildHallWorldObject)WorldObjectMaker.MakeWorldObject(def);
                h.Tile = g.Hall.Tile;
                h.GuildName = g.Name ?? ""; h.RadiusTiles = g.Hall.RadiusTiles;
                Find.WorldObjects.Add(h);
            }
            catch (Exception ex) { KmhLog.Warn($"Could not place guild hall marker at tile {g.Hall.Tile}: {ex.Message}"); }
        }
    }
}
