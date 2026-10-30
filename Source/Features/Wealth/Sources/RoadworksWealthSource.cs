using KMHPatch.Features.Roadworks;
using KMHPatch.Features.Roadworks.Dto;

namespace KMHPatch.Features.Wealth.Sources
{
    // Unspent road silver returns on cancel, so it counts; spent silver bought real road and is excluded.
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
