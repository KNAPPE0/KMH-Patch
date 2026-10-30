using System;
using System.Collections.Generic;
using KMHPatch.Features.PlayerStats.Dto;

namespace KMHPatch.Features.PlayerStats
{
    // Cleared on ResetSession so a previous server's data is never shown.
    public static class PlayerStatsCache
    {
        // Replaced wholesale each snapshot - read through the property, never cache the reference.
        public static List<PlayerLeaderboardEntry> Entries { get; private set; }
            = new List<PlayerLeaderboardEntry>();

        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static event Action Updated;

        internal static void Apply(PlayerStatsSnapshot snapshot)
        {
            Entries        = snapshot?.Entries ?? new List<PlayerLeaderboardEntry>();
            LastUpdatedUtc = DateTime.UtcNow;

            KmhCacheEvents.Raise(Updated, "Player stats");
        }

        internal static void Clear()
        {
            Entries        = new List<PlayerLeaderboardEntry>();
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
