using System;
using System.Collections.Generic;
using KMHPatch.Features.Treasury.Dto;

namespace KMHPatch.Features.Treasury
{
    public static class TreasuryCache
    {
        public static TreasurySnapshot Snapshot       { get; private set; }
        public static DateTime         LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        // Kept apart from Snapshot (whichever vault was fetched last) so wealth/threat can't swing on which dialog opened.
        public static TreasurySnapshot Personal { get; private set; }
        public static TreasurySnapshot Guild    { get; private set; }

        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        // A global quest delivery is paid from compact stock or from full-state copies (pork, gear), never both at once.
        public static int DeliverableCountOf(string defName) => DeliverableCountOf(Personal, defName);

        internal static int DeliverableCountOf(TreasurySnapshot vault, string defName)
            => Math.Max(CompactCountOf(vault?.Items, defName), PayloadCountOf(vault?.ItemPayloads, defName));

        // All materials/qualities of a def, since a by-def withdraw spends any of them.
        private static int CompactCountOf(Dictionary<string, int> items, string defName)
        {
            if (items == null || string.IsNullOrEmpty(defName)) return 0;
            long n = 0;
            foreach (KeyValuePair<string, int> kv in items)
                if (kv.Value > 0 && UI.ItemKeys.Matches(kv.Key, defName, "", 0)) n += kv.Value;
            return (int)Math.Min(int.MaxValue, n);
        }

        private static int PayloadCountOf(List<KMHPatch.Items.KmhThingPayload> payloads, string defName)
        {
            if (payloads == null || string.IsNullOrEmpty(defName)) return 0;
            long n = 0;
            foreach (KMHPatch.Items.KmhThingPayload p in payloads)
                if (p != null && p.StackCount > 0 && string.Equals(p.DefName, defName, StringComparison.OrdinalIgnoreCase)) n += p.StackCount;
            return (int)Math.Min(int.MaxValue, n);
        }

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
