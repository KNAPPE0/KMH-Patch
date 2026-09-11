using System;
using System.Collections.Generic;
using KMHPatch.Features.Quests;
using KMHPatch.Features.Quests.Dto;
using Verse;

namespace KMHPatch.Features.Wealth.Sources
{
    // Unsettled bounty escrow left the treasury but returns on cancel or expiry, so it is still the poster's and must count; settled states are already paid out or refunded.
    internal sealed class QuestWealthSource : IKmhWealthSource
    {
        public string Name => "Quests";

        public float SilverValue()
        {
            List<QuestEntry> quests = QuestCache.Snapshot?.Quests;
            string me = UI.KmhSession.Me;
            if (quests == null || string.IsNullOrEmpty(me)) return 0f;

            float v = 0f;
            foreach (QuestEntry q in quests)
            {
                if (q == null || !HoldsEscrow(q)) continue;
                if (!string.Equals(q.PosterUsername, me, StringComparison.OrdinalIgnoreCase)) continue;

                v += q.BountySilver;
                if (q.BountyItems != null)
                    foreach (KeyValuePair<string, int> kv in q.BountyItems)
                    {
                        ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(kv.Key);
                        if (def != null) v += def.BaseMarketValue * kv.Value;
                    }
            }
            return v;
        }

        // Mirrors the server's QuestStore.HoldsEscrow: pre-settlement states still hold the bounty.
        private static bool HoldsEscrow(QuestEntry q)
            => q.State == QuestEntry.StateOpen || q.State == QuestEntry.StateClaimed
            || q.State == QuestEntry.StateSubmitted || q.State == QuestEntry.StatePendingReview;
    }
}
