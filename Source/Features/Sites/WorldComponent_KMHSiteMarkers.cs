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
    // Keeps KMH site + guild-hall markers synced to the latest snapshots. They're server-driven, so transient
    // (never saved into RWT's shared world), and WorldObjects are main-thread only, so reconcile runs there.
    // Driven from BOTH WorldComponentTick (normal play) and Patch_Root_Update_KmhMarkers (every frame the world
    // exists), so markers show on the paused world view / reconnect / landing-site page, not just after a map load.
    public class WorldComponent_KMHSiteMarkers : WorldComponent
    {
        private static float _lastReconcileReal = -999f;

        // Tiles the current world doesn't have. Reconcile runs continuously, so warn once each instead of every pass.
        private static readonly HashSet<int> _warnedTiles = new HashSet<int>();

        // A server site can name a tile this planet has no index for (site made on a different world/seed, or a
        // smaller planet coverage). Resolving it throws deep inside RimWorld, so screen it out first.
        private static bool TileExists(int tile)
        {
            if (tile < 0) return false;
            try { return Find.WorldGrid != null && tile < Find.WorldGrid.TilesCount; }
            catch { return false; }
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

        // Called from both drivers. Throttled by real time so it's safe to call every frame; runs only when a world
        // (and its object holder) actually exists.
        internal static void TryReconcile(float minSeconds)
        {
            if (Find.World == null || Find.WorldObjects == null) return;
            float now = Time.realtimeSinceStartup;
            if (now - _lastReconcileReal < minSeconds && now >= _lastReconcileReal) return;   // (guard clock resets too)
            _lastReconcileReal = now;
            try { ReconcileSites(); }      catch (Exception ex) { KmhLog.Warn($"Site markers reconcile threw: {ex.Message}"); }
            try { ReconcileGuildHall(); }  catch (Exception ex) { KmhLog.Warn($"Guild hall marker reconcile threw: {ex.Message}"); }
        }

        private static void ReconcileSites()
        {
            List<KMHSiteWorldObject> existing = new List<KMHSiteWorldObject>();
            foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
                if (wo is KMHSiteWorldObject m) existing.Add(m);

            // Not connected / no data: drop any leftover markers (covers disconnect and loading a save offline) and stop.
            if (!KmhDispatcher.IsKmhServer || !SiteCache.HasSnapshot || SiteCache.Snapshot?.Sites == null)
            {
                foreach (KMHSiteWorldObject m in existing) Find.WorldObjects.Remove(m);
                return;
            }

            Dictionary<int, SiteEntry> desired = new Dictionary<int, SiteEntry>();
            foreach (SiteEntry s in SiteCache.Snapshot.Sites)
            {
                if (s == null || s.Tile < 0) continue;
                if (!TileExists(s.Tile)) { WarnMissingTileOnce(s.Tile, "Site"); continue; }
                desired[s.Tile] = s;
            }

            HashSet<int> have = new HashSet<int>();
            foreach (KMHSiteWorldObject m in existing)
            {
                int tile = m.Tile.tileId;
                if (have.Contains(tile)) { Find.WorldObjects.Remove(m); continue; }   // dedupe stray doubles
                if (desired.TryGetValue(tile, out SiteEntry s)) { Apply(m, s); have.Add(tile); }
                else Find.WorldObjects.Remove(m);
            }

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("KMHSite");
            if (def == null) return;
            foreach (KeyValuePair<int, SiteEntry> kv in desired)
            {
                if (have.Contains(kv.Key) || kv.Key < 0) continue;
                try
                {
                    KMHSiteWorldObject m = (KMHSiteWorldObject)WorldObjectMaker.MakeWorldObject(def);
                    m.Tile = kv.Key;
                    Apply(m, kv.Value);
                    Find.WorldObjects.Add(m);
                    have.Add(kv.Key);
                }
                catch (Exception ex) { KmhLog.Warn($"Could not place site marker at tile {kv.Key}: {ex.Message}"); }
            }
        }

        private static void Apply(KMHSiteWorldObject m, SiteEntry s)
        {
            m.SiteOwner      = string.IsNullOrEmpty(s.OwnerGuild) ? (s.OwnerUsername ?? "") : $"{s.OwnerGuild} (guild)";
            m.SiteItem       = UI.ItemLabels.ResolveLabel(s.ItemDefName);
            m.SiteWorkers    = s.Workers?.Count ?? 0;
            m.SiteMaxWorkers = s.MaxWorkers;
        }

        // One marker for the player's own guild hall, reconciled from the guild snapshot (same transient rules as
        // sites: gone on disconnect, moved/removed as the snapshot changes). Kept independent of the SITE snapshot so
        // a hall marker can show even when the player owns no sites.
        private static void ReconcileGuildHall()
        {
            List<Guilds.KMHGuildHallWorldObject> existing = new List<Guilds.KMHGuildHallWorldObject>();
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
