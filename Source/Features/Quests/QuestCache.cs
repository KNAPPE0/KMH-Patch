using System;
using KMHPatch.Features.Quests.Dto;

namespace KMHPatch.Features.Quests
{
    // Client cache for the most recent quest-board snapshot. Same pattern as the other feature caches - readers go
    // through Snapshot, subscribe to Updated for invalidation
    public static class QuestCache
    {
        public static QuestSnapshot Snapshot       { get; private set; }
        public static DateTime      LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(QuestSnapshot snapshot)
        {
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Quest");
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
