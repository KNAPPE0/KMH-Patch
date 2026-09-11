using System;
using KMHPatch.Features.WantBoard.Dto;

namespace KMHPatch.Features.WantBoard
{
    // Client cache for the latest want-board snapshot. Pushed on handshake, on every change, and on the dialog's auto-refresh tick.
    public static class WantCache
    {
        public static WantSnapshot Snapshot       { get; private set; }
        public static DateTime     LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool         HasSnapshot    => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(WantSnapshot snapshot)
        {
            // Two transports can deliver out of order; Clear() on disconnect is what lets a new server's lower revision still apply.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Want");
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
