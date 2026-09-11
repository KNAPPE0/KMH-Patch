using System.Collections.Generic;
using KMHPatch.Features.Roadworks.Dto;

namespace KMHPatch.Features.Roadworks
{
    // Reconciler's view of the network. Derived from RoadworksCache - never a second copy.
    internal static class KmhRoadNetwork
    {
        public static bool HasSnapshot => RoadworksCache.HasSnapshot;
        public static long Revision    => RoadworksCache.Snapshot?.Revision ?? 0;

        public static List<RoadSegmentDto> Segments
            => RoadworksCache.Snapshot?.Segments ?? new List<RoadSegmentDto>();

        public static string KeyOf(RoadSegmentDto s)
            => s == null ? null : RoadKeys.For(s.LayerA, s.TileA, s.LayerB, s.TileB);
    }
}
