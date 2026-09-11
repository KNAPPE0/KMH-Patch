using System;
using KMHPatch.Features.Marketplace.Dto;

namespace KMHPatch.Features.Marketplace
{
    // client cache for the latest marketplace snapshot
    public static class MarketplaceCache
    {
        public static MarketplaceSnapshot Snapshot       { get; private set; }
        public static DateTime            LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(MarketplaceSnapshot snapshot)
        {
            // Transports reorder; the disconnect clear is what lets a new server's lower revision still apply.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Marketplace");
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
