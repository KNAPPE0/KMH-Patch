using System;
using KMHPatch.Features.Seasons.Dto;

namespace KMHPatch.Features.Seasons
{
    // Caches the season archive snapshot (current leaders + past seasons + all-time server records).
    public static class SeasonArchiveCache
    {
        public static SeasonArchiveSnapshot Snapshot { get; private set; }
        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(SeasonArchiveSnapshot s)
        {
            Snapshot = s;
            LastUpdatedUtc = DateTime.UtcNow;
            try { Updated?.Invoke(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"Season archive subscriber threw: {ex.Message}"); }
        }
    }
}
