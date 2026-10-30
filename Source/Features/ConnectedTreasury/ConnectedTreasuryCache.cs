using System;
using System.Collections.Generic;
using KMHPatch.Features.ConnectedTreasury.Dto;

namespace KMHPatch.Features.ConnectedTreasury
{
    // The SERVER says what operations it carries out, GLOBAL what the account holds; neither is inferred from the other.
    public static class ConnectedTreasuryCache
    {
        public static ConnectedSnapshot Snapshot { get; private set; }
        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool HasSnapshot => Snapshot != null;

        // Read from Global with the player's own token. Null until a read succeeds, which is not the same as zero.
        public static ConnectedBalance Balance { get; private set; }
        public static DateTime BalanceUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static string BalanceError { get; private set; } = "";

        public static event Action Updated;

        public static bool ServerAvailable => Snapshot != null && Snapshot.Available;
        public static bool Linked => Snapshot != null && Snapshot.Linked;

        public static long PendingIn => Snapshot?.PendingIn ?? 0;
        public static long PendingOut => Snapshot?.PendingOut ?? 0;

        // While one is open the UI must not offer another, which is the same rule the server enforces.
        public static bool HasOpenOperation
        {
            get
            {
                if (Snapshot?.Operations == null) return false;
                foreach (ConnectedOperation o in Snapshot.Operations) if (o != null && o.Open) return true;
                return false;
            }
        }

        public static List<ConnectedOperation> Operations
            => Snapshot?.Operations ?? new List<ConnectedOperation>();

        internal static void Apply(ConnectedSnapshot snapshot)
        {
            // Transports reorder; the disconnect clear is what lets a new server's lower revision still apply.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
            Snapshot = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "ConnectedTreasury");
        }

        internal static void ApplyBalance(ConnectedBalance balance, string error)
        {
            Balance = balance;
            BalanceError = error ?? "";
            if (balance != null) BalanceUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "ConnectedTreasury");
        }

        // The balance is read with the linked account's own token; the server snapshot is not, and stays.
        static ConnectedTreasuryCache() => Global.KmhIdentityTeardown.Register("connected balance", ForgetBalance);

        internal static void ForgetBalance()
        {
            Balance = null;
            BalanceError = "";
            BalanceUpdatedUtc = DateTime.MinValue;
            KmhCacheEvents.Raise(Updated, "ConnectedTreasury");
        }

        public static void Clear()
        {
            Snapshot = null;
            Balance = null;
            BalanceError = "";
            LastUpdatedUtc = DateTime.MinValue;
            BalanceUpdatedUtc = DateTime.MinValue;
            KmhCacheEvents.Raise(Updated, "ConnectedTreasury");
        }
    }
}
