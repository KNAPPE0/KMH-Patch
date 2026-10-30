using System;
using KMHPatch.Features.Quests.Dto;

namespace KMHPatch.Features.Quests
{
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
            Snapshot       = MakeInert(snapshot);
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Quest");
        }

        // Written by players and drawn as rich text, so markup is neutralised once here; key fields stay raw.
        internal static QuestSnapshot MakeInert(QuestSnapshot s)
        {
            if (s?.Quests == null) return s;
            foreach (QuestEntry q in s.Quests)
            {
                if (q == null) continue;
                q.Title                   = UI.KmhDisplayText.Inert(q.Title);
                q.Description             = UI.KmhDisplayText.Inert(q.Description);
                q.EscortTargetDescription = UI.KmhDisplayText.Inert(q.EscortTargetDescription);
                q.ProofText               = UI.KmhDisplayText.Inert(q.ProofText);
                q.ReviewNote              = UI.KmhDisplayText.Inert(q.ReviewNote);
            }
            return s;
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
