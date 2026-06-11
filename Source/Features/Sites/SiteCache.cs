using System;
using KMHPatch.Features.Sites.Dto;

namespace KMHPatch.Features.Sites
{
    // client cache for the latest custom-sites snapshot
    public static class SiteCache
    {
        public static SiteSnapshot Snapshot       { get; private set; }
        public static DateTime     LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(SiteSnapshot snapshot)
        {
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            try { Updated?.Invoke(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"Cache subscriber threw: {ex.Message}"); }
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
