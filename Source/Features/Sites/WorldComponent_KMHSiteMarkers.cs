using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.SubProtocol;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Keeps KMH site markers on the world map in sync with the site snapshot. KMH sites are server-driven, so
    // markers are transient. WorldObjects can only be touched from the main thread (never the network receive
    // thread), so we reconcile here on a throttled WorldComponentTick rather than reacting to the snapshot event
    // directly
    //
    // Auto-instantiated by RimWorld for every WorldComponent subclass - no def needed
    public class WorldComponent_KMHSiteMarkers : WorldComponent
    {
        private const int IntervalTicks = 120; // ~2s
        private int _next;

        public WorldComponent_KMHSiteMarkers(World world) : base(world) { }

        public override void WorldComponentTick()
        {
            if (Find.TickManager.TicksGame < _next) return;
            _next = Find.TickManager.TicksGame + IntervalTicks;
            try { Reconcile(); }
            catch (Exception ex) { KmhLog.Warn($"Site markers reconcile threw: {ex.Message}"); }
        }

        private void Reconcile()
        {
            List<KMHSiteWorldObject> existing = new List<KMHSiteWorldObject>();
            foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
                if (wo is KMHSiteWorldObject m) existing.Add(m);

            // Not connected / no data: drop any leftover markers (covers disconnect and loading a save offline) and
            // stop
            if (!KmhDispatcher.IsKmhServer || !SiteCache.HasSnapshot || SiteCache.Snapshot?.Sites == null)
            {
                foreach (KMHSiteWorldObject m in existing) Find.WorldObjects.Remove(m);
                return;
            }

            Dictionary<int, SiteEntry> desired = new Dictionary<int, SiteEntry>();
            foreach (SiteEntry s in SiteCache.Snapshot.Sites)
                if (s != null && s.Tile >= 0) desired[s.Tile] = s;

            HashSet<int> have = new HashSet<int>();
            foreach (KMHSiteWorldObject m in existing)
            {
                int tile = m.Tile.tileId;
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
    }
}
