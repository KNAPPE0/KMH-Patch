using KMHPatch.Features.Roadworks;
using KMHPatch.Features.Roadworks.Dto;

namespace KMHPatch.Features.Wealth.Sources
{
    // Committed but unspent road silver returns on cancel, so it counts; spent silver bought road that exists and is deliberately excluded.
    internal sealed class RoadworksWealthSource : IKmhWealthSource
    {
        public string Name => "Roadworks";

        public float SilverValue()
        {
            RoadworksSnapshot s = RoadworksCache.Snapshot;
            return s == null ? 0f : s.EscrowSilver;
        }
    }
}
