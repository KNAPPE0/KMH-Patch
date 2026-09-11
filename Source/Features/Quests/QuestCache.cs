using System;
using KMHPatch.Features.Quests.Dto;

namespace KMHPatch.Features.Quests
{
    // Client cache for the most recent quest-board snapshot.
    public static class QuestCache
    {
        public static QuestSnapshot Snapshot       { get; private set; }
        public static DateTime      LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(QuestSnapshot snapshot)
        {
            // Two transports can deliver out of order; Clear() on disconnect lets a new server's lower revision apply.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
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
