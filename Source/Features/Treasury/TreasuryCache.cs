using System;
using KMHPatch.Features.Treasury.Dto;

namespace KMHPatch.Features.Treasury
{
    // Client-side cache for the most recent treasury snapshot. Static
    // singleton-style so open dialogs read straight from properties + subscribe to Updated
    public static class TreasuryCache
    {
        public static TreasurySnapshot Snapshot       { get; private set; }
        public static DateTime         LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        // True once at least one snapshot has been received. Dialogs use this to differentiate "loading" from
        // "empty"
        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        // Pending (deposited but not yet save-finalized) totals, for honest "why can't I use this yet" messaging.
        public static int PendingSilver()
        {
            var pd = Snapshot?.PendingDeposits; int s = 0;
            if (pd != null) foreach (PendingDeposit p in pd) if (p != null) s += p.Silver;
            return s;
        }
        public static int PendingItemUnits()
        {
            var pd = Snapshot?.PendingDeposits; int n = 0;
            if (pd != null) foreach (PendingDeposit p in pd) if (p != null) n += p.Qty;
            return n;
        }
        public static bool HasPendingItems() => PendingItemUnits() > 0;

        internal static void Apply(TreasurySnapshot snapshot)
        {
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Treasury");
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
