using System;
using System.Collections.Generic;
using KMHPatch.Features.Treasury.Dto;

namespace KMHPatch.Features.Treasury
{
    // Client-side cache of the latest treasury snapshot; dialogs read the properties and subscribe to Updated.
    public static class TreasuryCache
    {
        public static TreasurySnapshot Snapshot       { get; private set; }
        public static DateTime         LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        // Kept apart from Snapshot (whichever vault was fetched last) so wealth/threat can't swing on which dialog opened.
        public static TreasurySnapshot Personal { get; private set; }
        public static TreasurySnapshot Guild    { get; private set; }

        // Lets dialogs tell "loading" from "empty".
        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        // All materials/qualities of a def, since a by-def withdraw spends any of them; payload copies excluded.
        public static int CompactCountOf(string defName)
        {
            var items = Personal?.Items;
            if (items == null || string.IsNullOrEmpty(defName)) return 0;
            int n = 0;
            foreach (KeyValuePair<string, int> kv in items)
                if (UI.ItemKeys.Matches(kv.Key, defName, "", 0)) n += kv.Value;
            return n;
        }

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

        // Per-vault: both arrive on the same kind, so one shared counter would make each drop the other.
        private static long _personalRevision;
        private static long _guildRevision;

        internal static void Apply(TreasurySnapshot snapshot)
        {
            if (snapshot == null) return;
            // Two transports deliver out of order: a stale balance read landing late puts spent silver back on screen.
            long applied = snapshot.IsGuildOwned ? _guildRevision : _personalRevision;
            if (applied > 0 && snapshot.Revision < applied) return;
            if (snapshot.IsGuildOwned) { _guildRevision = snapshot.Revision; Guild = snapshot; }
            else                       { _personalRevision = snapshot.Revision; Personal = snapshot; }

            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Treasury");
        }

        internal static void Clear()
        {
            Snapshot       = null;
            Personal       = null;
            Guild          = null;
            _personalRevision = 0;
            _guildRevision    = 0;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
