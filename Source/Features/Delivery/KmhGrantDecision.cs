namespace KMHPatch.Features.Delivery
{
    internal enum KmhGrantOutcome
    {
        Delivered,
        Held,            // nothing placed (no drop site) - safe to hold and retry when one appears
        Undeliverable,   // a drop site existed but placement still failed - do NOT retry (a partial retry could duplicate)
    }

    // What the player is told when a grant cannot land in full; shared by the immediate and the held path.
    internal static class KmhGrantNotice
    {
        private static readonly System.Collections.Generic.HashSet<string> _blockedShown = new System.Collections.Generic.HashSet<string>();

        // Nothing was placed and nothing is lost: the server keeps owing it, so it is said once a session, not as an alarm.
        public static void Blocked(string desc, string why, string deliveryId)
        {
            if (!_blockedShown.Add(string.IsNullOrEmpty(deliveryId) ? desc : deliveryId)) return;
            Notifications.KmhNotifications.Rejected(UI.KmhText.Get("KMH_Grant_Blocked",
                "{0} could not arrive. {1} It stays owed to you and arrives when you join with that content active.", desc, why));
        }

        // Some of it landed before a stack failed: recorded as received so a replay cannot place it again.
        public static void PartlyDelivered(string desc, string failed)
            => Notifications.KmhNotifications.Alert("Withdrawal partly delivered",
                $"Colony Nexus delivered part of {desc}, but {failed} could not be placed. It is not sent again, so nothing is " +
                "duplicated. Take a screenshot and ask the server owner to restore what is missing.");
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
