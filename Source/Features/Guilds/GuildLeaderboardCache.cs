using System;
using KMHPatch.Features.Guilds.Dto;

namespace KMHPatch.Features.Guilds
{
    // Client cache for the cross-guild leaderboard snapshot. Separate from GuildCache (which holds the caller's own
    // guild) so the in-game guild leaderboard dialog has a place to observe-without-mutating-the-other- cache
    public static class GuildLeaderboardCache
    {
        public static GuildLeaderboardSnapshot Snapshot       { get; private set; }
        public static DateTime                 LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(GuildLeaderboardSnapshot snapshot)
        {
            Snapshot       = snapshot ?? new GuildLeaderboardSnapshot();
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
