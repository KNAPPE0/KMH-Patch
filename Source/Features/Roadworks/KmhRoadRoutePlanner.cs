using System.Collections.Generic;
using KMHPatch.Diagnostics;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Roadworks
{
    // The headless server cannot judge adjacency, so validating a route here is load-bearing rather than cosmetic.
    internal static class KmhRoadRoutePlanner
    {
        public static bool TryPlan(PlanetTile from, PlanetTile to, int maxSegments,
                                   out List<PlanetTile> route, out string reason)
        {
            route = null; reason = null;
            if (!from.Valid || !to.Valid) { reason = "Pick a tile on this planet."; return false; }
            if (from == to) { reason = "Pick a tile away from the site."; return false; }
            // A cross-layer key would name two unconnected places.
            if (from.LayerId() != to.LayerId()) { reason = "A road cannot run between planet layers."; return false; }

            List<PlanetTile> nodes = FindNodes(from, to);
            if (nodes == null || nodes.Count < 2) { reason = "No overland route to that tile."; return false; }
            if (nodes.Count - 1 > maxSegments)
            { reason = $"That route is too long ({nodes.Count - 1} segments, max {maxSegments})."; return false; }

            // Trusted for cost, not for the shape KMH persists: a gap would key a segment between tiles that do not touch, and the server cannot catch it.
            WorldGrid grid = Find.WorldGrid;
            for (int i = 0; i + 1 < nodes.Count; i++)
                if (grid == null || !grid.IsNeighbor(nodes[i], nodes[i + 1]))
                { reason = "That route skips a tile - try a nearer destination."; return false; }

            route = nodes;
            return true;
        }

        private static List<PlanetTile> FindNodes(PlanetTile from, PlanetTile to)
        {
            WorldPath path = null;
            try
            {
                path = from.Layer?.Pather?.FindPath(from, to, null, null);
                if (path == null || !path.Found) return null;

                // NodesReversed runs destination -> start.
                var nodes = new List<PlanetTile>(path.NodesReversed);
                nodes.Reverse();
                return nodes;
            }
            catch (System.Exception ex)
            {
                KmhLog.Warn("Roadworks: route planning failed - " + ex.Message);
                return null;
            }
            finally { try { path?.ReleaseToPool(); } catch { } }
        }

        // Flat (layer, tile) form used by the quote and the wire.
        public static List<(int layer, int tile)> Flatten(List<PlanetTile> route)
        {
            var outp = new List<(int, int)>();
            if (route == null) return outp;
            foreach (PlanetTile t in route) outp.Add((t.LayerId(), t.tileId));
            return outp;
        }
    }

    internal static class PlanetTileExtensions
    {
        // PlanetTile.layerId is private; the layer object exposes it.
        public static int LayerId(this PlanetTile t)
        {
            try { return t.Layer?.LayerID ?? 0; } catch { return 0; }
        }
    }
}
