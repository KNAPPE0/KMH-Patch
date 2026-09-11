using System;
using KMHPatch.Features.Guilds.Dto;

namespace KMHPatch.Features.Guilds
{
    // Separate from GuildCache, which holds only the caller's own guild.
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
            KmhCacheEvents.Raise(Updated, "Guild leaderboard");
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
