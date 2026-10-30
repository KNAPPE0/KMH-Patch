using System;
using System.Collections.Generic;
using KMHPatch.Features.ConnectedGuild.Dto;

namespace KMHPatch.Features.ConnectedGuild
{
    // Server-only source, including Global's last word on the balance; the client computes and asserts nothing from it.
    public static class ConnectedGuildCache
    {
        public static ConnectedGuildSnapshot Snapshot { get; private set; }
        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        public static bool ServerAvailable => Snapshot != null && Snapshot.Available;
        public static bool Linked          => Snapshot != null && Snapshot.Linked;
        public static bool InGuild         => Snapshot != null && Snapshot.InGuild;
        public static bool NeedsReproof    => Snapshot != null && Snapshot.NeedsReproof;
        public static bool CanMoveValue    => Snapshot != null && Snapshot.CanMoveValue;

        public static long PendingIn  => Snapshot?.PendingIn ?? 0;
        public static long PendingOut => Snapshot?.PendingOut ?? 0;

        // One open operation per guild, as the server enforces, so another officer's operation blocks this one too.
        public static bool HasOpenOperation
        {
            get
            {
                if (Snapshot?.Operations == null) return false;
                foreach (ConnectedGuildOperation o in Snapshot.Operations) if (o != null && o.Open) return true;
                return false;
            }
        }

        public static List<ConnectedGuildOperation> Operations
            => Snapshot?.Operations ?? new List<ConnectedGuildOperation>();

        internal static void Apply(ConnectedGuildSnapshot snapshot)
        {
            // Transports reorder; the disconnect clear is what lets a new server's lower revision still apply.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
            Snapshot = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "ConnectedGuild");
        }

        public static void Clear()
        {
            Snapshot = null;
            LastUpdatedUtc = DateTime.MinValue;
            KmhCacheEvents.Raise(Updated, "ConnectedGuild");
        }
    }
}
