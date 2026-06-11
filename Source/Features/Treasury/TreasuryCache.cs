using System;
using KMHPatch.Features.Treasury.Dto;

namespace KMHPatch.Features.Treasury
{
    // Client-side cache for the most recent treasury snapshot. Static
    // singleton-style so open dialogs read straight from properties + subscribe to Updated
    public static class TreasuryCache
    {
        public static TreasurySnapshot Snapshot       { get; private set; }
        public static DateTime         LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        // True once at least one snapshot has been received. Dialogs use this to differentiate "loading" from
        // "empty"
        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(TreasurySnapshot snapshot)
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
