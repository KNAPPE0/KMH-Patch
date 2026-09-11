using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Roadworks.Dto;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Roadworks
{
    // KMH's roads are indistinguishable from worldgen's after a reload, so without this saved claim record they would read as foreign and never be cleaned up.
    public class WorldComponent_KMHRoads : WorldComponent
    {
        private static WorldComponent_KMHRoads _instance;

        // key -> what we wrote. Flattened for Scribe; Applied stays Verse-free so the rules stay testable.
        private readonly Dictionary<string, KmhRoadReconcile.Applied> _applied =
            new Dictionary<string, KmhRoadReconcile.Applied>();

        // How often a key came back as somebody else's. Session-only: a new run deserves a fresh try at laying the road.
        private readonly Dictionary<string, int> _rebuilds = new Dictionary<string, int>();

        private List<string> _scribeKeys = new List<string>();
        private List<string> _scribeDefs = new List<string>();

        private long  _appliedRevision = -1;
        private float _lastRealTime    = -999f;

        public WorldComponent_KMHRoads(RimWorld.Planet.World world) : base(world) { _instance = this; }

        public override void FinalizeInit(bool fromLoad)
        {
            _instance = this;
            _appliedRevision = -1;   // re-apply against the world we just loaded
        }

        public override void WorldComponentTick() => TryReconcile(2f);

        // A new server numbers revisions independently, but the claims are kept: they record roads KMH wrote that still need cleaning up.
        public static void ForgetApplied()
        {
            if (_instance != null) _instance._appliedRevision = -1;
        }

        public static void TryReconcile(float minIntervalSeconds)
        {
            WorldComponent_KMHRoads c = _instance;
            if (c == null || Find.World == null || Find.WorldGrid == null) return;
            if (RealTime.LastRealTime - c._lastRealTime < minIntervalSeconds) return;
            c._lastRealTime = RealTime.LastRealTime;
            c.Reconcile();
        }

        private const float VerifySeconds = 2f;
        private float _lastVerifyReal = -999f;
        private bool  _saidDrift;

        // The revision says what the server wants, never what the planet still shows: a road wiped behind our back went unnoticed forever while the revision sat unchanged.
        private bool DriftedFromClaims()
        {
            if (_applied.Count == 0) return false;
            float now = RealTime.LastRealTime;
            if (now - _lastVerifyReal < VerifySeconds && now >= _lastVerifyReal) return false;
            _lastVerifyReal = now;

            foreach (KeyValuePair<string, KmhRoadReconcile.Applied> kv in _applied)
            {
                if (!TryParseKey(kv.Key, out int la, out int ta, out int lb, out int tb)) continue;
                try
                {
                    var a = new PlanetTile(ta, la);
                    var b = new PlanetTile(tb, lb);
                    if (!a.Valid || !b.Valid || !(a.Tile is SurfaceTile)) continue;
                    if (string.Equals(ExistingRoad(a, b)?.defName, kv.Value.AppliedDefName)) continue;
                }
                catch { continue; }

                if (!_saidDrift)
                {
                    _saidDrift = true;
                    KmhLog.Info("Roadworks: a KMH road was removed from the map by something else - putting it back, "
                              + "and watching for it from here on.");
                }
                return true;
            }
            return false;
        }

        private void Reconcile()
        {
            // No snapshot means not heard from the server yet; reading it as "no roads" would tear up every KMH road on a slow connect.
            if (!KmhRoadNetwork.HasSnapshot) return;
            if (KmhRoadNetwork.Revision == _appliedRevision && !DriftedFromClaims()) return;

            var want = new Dictionary<string, RoadSegmentDto>();
            foreach (RoadSegmentDto s in KmhRoadNetwork.Segments)
            {
                string k = KmhRoadNetwork.KeyOf(s);
                if (k != null) want[k] = s;
            }

            // What the server wants, plus everything we claim, so a dropped segment still gets cleaned up.
            var keys = new HashSet<string>(want.Keys);
            foreach (string k in _applied.Keys) keys.Add(k);

            var dirtyLayers = new HashSet<PlanetLayer>();
            int added = 0, removed = 0, deferred = 0, abandoned = 0, stuck = 0;

            foreach (string key in keys)
            {
                want.TryGetValue(key, out RoadSegmentDto seg);
                _applied.TryGetValue(key, out KmhRoadReconcile.Applied rec);

                if (!TryResolvePair(key, seg, rec, out PlanetTile a, out PlanetTile b)) continue;

                string  wantTier = seg?.Tier;
                RoadDef wantDef  = wantTier == null ? null : KmhRoadDefs.ForTier(wantTier);
                RoadDef existing = ExistingRoad(a, b);

                _rebuilds.TryGetValue(key, out int rebuilds);
                KmhRoadReconcile.Verdict v = KmhRoadReconcile.Decide(
                    wantTier, wantDef?.defName, wantDef?.priority ?? 0, rec,
                    existing?.defName, existing?.priority ?? 0, rebuilds);

                if (v.ReleasedStaleClaim) abandoned++;
                if (v.Contested) _rebuilds[key] = rebuilds + 1;

                switch (v.Action)
                {
                    case KmhRoadReconcile.Action.Add:
                    case KmhRoadReconcile.Action.Upgrade:
                        SetRoad(a, b, wantDef);
                        added++;
                        if (ExistingRoad(a, b) == wantDef) stuck++;   // read back: a write that did not take says so here
                        break;
                    case KmhRoadReconcile.Action.Restore:
                        SetRoad(a, b, DefDatabase<RoadDef>.GetNamedSilentFail(v.WriteDefName));
                        removed++;
                        break;
                    case KmhRoadReconcile.Action.Remove:
                        SetRoad(a, b, null);
                        removed++;
                        break;
                    case KmhRoadReconcile.Action.Defer:
                        deferred++;
                        break;
                }

                if (v.Record == null) _applied.Remove(key); else _applied[key] = v.Record;

                if (v.Action != KmhRoadReconcile.Action.None && v.Action != KmhRoadReconcile.Action.Defer)
                {
                    Recalculate(a); Recalculate(b);
                    dirtyLayers.Add(a.Layer); dirtyLayers.Add(b.Layer);
                }
            }

            // A pass that released claims has not converged yet; re-check next tick rather than trusting the revision.
            if (abandoned == 0) _appliedRevision = KmhRoadNetwork.Revision;

            int redrawn = dirtyLayers.Count > 0 ? MarkRoadsDirty(dirtyLayers) : 0;

            if (added + removed + deferred + abandoned > 0)
                KmhLog.Debug($"Roadworks: {added} road(s) written ({stuck} confirmed on the tile), {removed} cleared, " +
                             $"{deferred} left to another source, {abandoned} claim(s) released, " +
                             $"{redrawn} map layer(s) redrawn (revision {KmhRoadNetwork.Revision}).");
        }

        // SetDirty<T>(layer) matches the PlanetLayer the draw layer holds, so a tile carrying another instance silently marks nothing. Counted here, swept when none matched.
        private static bool _warnedNoLayer;

        private static int MarkRoadsDirty(HashSet<PlanetLayer> layers)
        {
            int marked = 0, roadLayers = 0;
            try
            {
                // Typed as the BASE: AllDrawLayers holds global layers too, and naming the subclass here threw a cast on the first one.
                foreach (WorldDrawLayerBase dl in Find.World.renderer.AllDrawLayers)
                {
                    if (!(dl is WorldDrawLayer_Roads roads)) continue;
                    roadLayers++;
                    if (!layers.Contains(roads.planetLayer)) continue;
                    roads.SetDirty(); marked++;
                }
                if (marked == 0 && roadLayers > 0)
                {
                    foreach (WorldDrawLayerBase dl in Find.World.renderer.AllDrawLayers)
                        if (dl is WorldDrawLayer_Roads roads) { roads.SetDirty(); marked++; }
                }
                if (roadLayers == 0 && !_warnedNoLayer)
                {
                    _warnedNoLayer = true;
                    KmhLog.Warn("Roadworks: this world has no roads draw layer, so KMH roads cannot be shown.");
                }
            }
            catch (System.Exception ex) { KmhLog.Warn($"Roadworks: could not refresh the road map layer: {ex.Message}"); }
            return marked;
        }

        // A segment can name tiles this planet lacks (built on another world), and only the client knows adjacency.
        private static readonly HashSet<string> _warned = new HashSet<string>();

        private bool TryResolvePair(string key, RoadSegmentDto seg, KmhRoadReconcile.Applied rec,
                                    out PlanetTile a, out PlanetTile b)
        {
            a = PlanetTile.Invalid; b = PlanetTile.Invalid;
            if (seg == null && rec == null) return false;

            int la, ta, lb, tb;
            if (seg != null) { la = seg.LayerA; ta = seg.TileA; lb = seg.LayerB; tb = seg.TileB; }
            else if (!TryParseKey(key, out la, out ta, out lb, out tb)) return false;

            try
            {
                a = new PlanetTile(ta, la);
                b = new PlanetTile(tb, lb);
                if (!a.Valid || !b.Valid) return WarnOnce(key, "names a tile this planet does not have");
                if (!(a.Tile is SurfaceTile) || !(b.Tile is SurfaceTile))
                    return WarnOnce(key, "names a tile that carries no roads");
                if (!Find.WorldGrid.IsNeighbor(a, b)) return WarnOnce(key, "names two tiles that are not neighbours here");
            }
            catch { return WarnOnce(key, "could not be resolved on this planet"); }
            return true;
        }

        private static bool WarnOnce(string key, string why)
        {
            if (_warned.Add(key)) KmhLog.Warn($"Roadworks: road segment {key} {why} - skipped.");
            return false;
        }

        private static bool TryParseKey(string key, out int la, out int ta, out int lb, out int tb)
        {
            la = ta = lb = tb = -1;
            if (string.IsNullOrEmpty(key)) return false;
            string[] halves = key.Split('|');
            if (halves.Length != 2) return false;
            return TryParseTile(halves[0], out la, out ta) && TryParseTile(halves[1], out lb, out tb);
        }

        private static bool TryParseTile(string s, out int layer, out int tile)
        {
            layer = tile = -1;
            string[] parts = (s ?? "").Split(':');
            return parts.Length == 2 && int.TryParse(parts[0], out layer) && int.TryParse(parts[1], out tile);
        }

        private static RoadDef ExistingRoad(PlanetTile a, PlanetTile b)
        {
            List<SurfaceTile.RoadLink> roads = (a.Tile as SurfaceTile)?.potentialRoads;
            if (roads == null) return null;
            foreach (SurfaceTile.RoadLink l in roads)
                if (l.neighbor == b) return l.road;
            return null;
        }

        // Both directions together; a null def removes the link.
        private static void SetRoad(PlanetTile a, PlanetTile b, RoadDef def)
        {
            SetOneWay(a, b, def);
            SetOneWay(b, a, def);
        }

        private static void SetOneWay(PlanetTile from, PlanetTile to, RoadDef def)
        {
            SurfaceTile t = from.Tile as SurfaceTile;
            if (t == null) return;
            if (t.potentialRoads == null)
            {
                if (def == null) return;
                t.potentialRoads = new List<SurfaceTile.RoadLink>();
            }
            for (int i = t.potentialRoads.Count - 1; i >= 0; i--)
                if (t.potentialRoads[i].neighbor == to) t.potentialRoads.RemoveAt(i);
            if (def != null)
                t.potentialRoads.Add(new SurfaceTile.RoadLink { neighbor = to, road = def });
        }

        private static void Recalculate(PlanetTile tile)
        {
            try { Find.WorldPathGrid?.RecalculatePerceivedMovementDifficultyAt(tile, out _, null); } catch { }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                _scribeKeys = new List<string>();
                _scribeDefs = new List<string>();
                foreach (KeyValuePair<string, KmhRoadReconcile.Applied> kv in _applied)
                {
                    _scribeKeys.Add(kv.Key);
                    _scribeDefs.Add(kv.Value.AppliedDefName + ">" + (kv.Value.PreviousDefName ?? ""));
                }
            }
            Scribe_Collections.Look(ref _scribeKeys, "kmhRoadKeys", LookMode.Value);
            Scribe_Collections.Look(ref _scribeDefs, "kmhRoadDefs", LookMode.Value);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;

            _applied.Clear();
            if (_scribeKeys == null || _scribeDefs == null) return;
            for (int i = 0; i < _scribeKeys.Count && i < _scribeDefs.Count; i++)
            {
                string[] parts = (_scribeDefs[i] ?? "").Split('>');
                if (parts.Length != 2 || parts[0].Length == 0) continue;
                _applied[_scribeKeys[i]] = new KmhRoadReconcile.Applied
                { AppliedDefName = parts[0], PreviousDefName = parts[1].Length == 0 ? null : parts[1] };
            }
        }
    }
}
