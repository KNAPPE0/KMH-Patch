using System;
using System.Collections.Generic;
using KMHPatch.Features.PlayerStats.Dto;

namespace KMHPatch.Features.PlayerStats
{
    // Client-side cache for the most recent player-leaderboard snapshot.
    //
    // Static singleton-style, like the other client caches. Open dialogs read Entries directly and subscribe to
    // Updated for invalidation.
    //
    // Cleared by PlayerStatsHandler when KmhDispatcher.ResetSession fires, so we never show stale data from a
    // previous server.
    public static class PlayerStatsCache
    {
        // Latest snapshot from the server. Empty until first snapshot lands. Replaced wholesale on each snapshot -
        // readers should always go through this property, not cache a reference
        public static List<PlayerLeaderboardEntry> Entries { get; private set; }
            = new List<PlayerLeaderboardEntry>();

        // UTC timestamp of the last snapshot we received. DateTime.MinValue before any snapshot has arrived
        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        // Fires after Entries has been replaced. Open dialogs subscribe so they can invalidate any filter/sort
        // caches and flip a "● live" indicator instantly instead of waiting for their next poll tick
        public static event Action Updated;

        internal static void Apply(PlayerStatsSnapshot snapshot)
        {
            Entries        = snapshot?.Entries ?? new List<PlayerLeaderboardEntry>();
            LastUpdatedUtc = DateTime.UtcNow;

            // Wrap in try so a misbehaving subscriber can't poison the packet-receive thread.
            try { Updated?.Invoke(); } catch (System.Exception ex) { Diagnostics.KmhLog.Warn($"Cache subscriber threw: {ex.Message}"); }
        }

        internal static void Clear()
        {
            Entries        = new List<PlayerLeaderboardEntry>();
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
