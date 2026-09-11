using System.Collections.Generic;
using KMHPatch.Features.Roadworks.Dto;

namespace KMHPatch.Features.Roadworks
{
    // What a route costs, worked out the way the server charges. RimWorld-free so it stays testable.
    internal static class KmhRoadQuote
    {
        // Segment keys in route order; the same tile twice is not a segment.
        public static List<string> RouteKeys(IList<(int layer, int tile)> route)
        {
            var keys = new List<string>();
            if (route == null) return keys;
            for (int i = 0; i + 1 < route.Count; i++)
            {
                (int la, int ta) = route[i];
                (int lb, int tb) = route[i + 1];
                if (la == lb && ta == tb) continue;
                keys.Add(RoadKeys.For(la, ta, lb, tb));
            }
            return keys;
        }

        public static HashSet<string> BuiltKeys(RoadworksSnapshot snap)
        {
            var built = new HashSet<string>(System.StringComparer.Ordinal);
            if (snap?.Segments == null) return built;
            foreach (RoadSegmentDto s in snap.Segments)
                if (s != null) built.Add(RoadKeys.For(s.LayerA, s.TileA, s.LayerB, s.TileB));
            return built;
        }

        // Only genuinely new segments are charged. An unpriced tier quotes 0 rather than guessing.
        public static int SilverFor(RoadworksSnapshot snap, string tier, List<string> routeKeys)
        {
            if (snap?.SilverPerSegment == null) return 0;
            if (!snap.SilverPerSegment.TryGetValue(KmhRoadDefs.Normalize(tier), out int per)) return 0;
            int newCount = RoadKeys.NewSegmentKeys(routeKeys, BuiltKeys(snap)).Count;
            return per * newCount;
        }

        // Why a route cannot be sent, or null. Mirrors the server's refusals so the player is told before paying.
        public static string RefusalFor(RoadworksSnapshot snap, string tier, List<string> routeKeys)
        {
            if (snap == null) return "Still waiting for the server's road network.";
            if (!snap.AllowRoadworks) return "Roadworks is turned off on this server.";
            if (routeKeys == null || routeKeys.Count == 0) return "A route needs at least two tiles.";
            if (routeKeys.Count > snap.MaxRouteSegments)
                return $"That route is too long ({routeKeys.Count} segments, max {snap.MaxRouteSegments}).";
            if (RoadKeys.NewSegmentKeys(routeKeys, BuiltKeys(snap)).Count == 0) return "That route is already built.";
            return null;
        }
    }
}
