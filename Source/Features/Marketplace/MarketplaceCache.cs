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
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            try { Updated?.Invoke(); } catch (System.Exception ex) { Diagnostics.KmhLog.Warn($"Cache subscriber threw: {ex.Message}"); }
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
