using System;
using System.Collections.Generic;
using KMHPatch.Features.PlayerStats.Dto;

namespace KMHPatch.Features.PlayerStats
{
    // Caches the flattened colonist roster (every colony's colonists) for the per-skill Colonist Records board.
    public static class ColonistRosterCache
    {
        public static ColonistRosterSnapshot Snapshot { get; private set; }
        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        public static List<ColonistEntry> Colonists => Snapshot?.Colonists ?? new List<ColonistEntry>();

        internal static void Apply(ColonistRosterSnapshot s)
        {
            Snapshot = s;
            LastUpdatedUtc = DateTime.UtcNow;
            try { Updated?.Invoke(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"Colonist roster subscriber threw: {ex.Message}"); }
        }

        internal static void Clear() { Snapshot = null; LastUpdatedUtc = DateTime.MinValue; }
    }
}
