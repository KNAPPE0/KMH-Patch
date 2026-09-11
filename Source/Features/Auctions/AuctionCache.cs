using System;
using KMHPatch.Features.Auctions.Dto;

namespace KMHPatch.Features.Auctions
{
    // Client cache for the latest auction snapshot. Pushed on handshake, on every change, and on the dialog's auto-refresh tick.
    public static class AuctionCache
    {
        public static AuctionSnapshot Snapshot       { get; private set; }
        public static DateTime        LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool            HasSnapshot    => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(AuctionSnapshot snapshot)
        {
            // Transports reorder; the disconnect clear is what lets a new server's lower revision still apply.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Auction");
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
