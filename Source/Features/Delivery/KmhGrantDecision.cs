namespace KMHPatch.Features.Delivery
{
    internal enum KmhGrantOutcome
    {
        Delivered,       // placed into the colony/caravan now
        Held,            // nothing placed (no drop site) - safe to hold and retry when one appears
        Undeliverable,   // a drop site existed but placement still failed - do NOT retry (a partial retry could duplicate)
    }

    // A leg is held for retry only when nothing was placed, because a partially placed one must never be re-attempted.
    internal static class KmhGrantDecision
    {
        public static KmhGrantOutcome Decide(bool delivered, bool canDeliverNow)
        {
            if (delivered) return KmhGrantOutcome.Delivered;
            return canDeliverNow ? KmhGrantOutcome.Undeliverable : KmhGrantOutcome.Held;
        }
    }
}
