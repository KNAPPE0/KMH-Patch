using KMHPatch.Diagnostics;
using KMHPatch.Features.Auctions.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Auctions
{
    // Receives kmh.auction.snapshot into AuctionCache + the post/bid/cancel sends.
    // Bids draw from the player's treasury server-side, so there's nothing to remove client-side - just send the amount.
    internal static class AuctionHandler
    {
        public static void Register() => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.AuctionSnapshot, OnSnapshot);

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.AuctionRequest, null);

        public static bool TryPost(string itemDefName, string stuffDefName, int qualityIndex, int qty,
            long startingBid, long minIncrement, long buyout, int hours, string visibility)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.AuctionPost, new
            {
                item_def_name  = itemDefName ?? "",
                stuff_def_name = stuffDefName ?? "",
                quality_index  = qualityIndex,
                qty,
                starting_bid   = startingBid,
                min_increment  = minIncrement,
                buyout_silver  = buyout,
                hours,
                visibility     = visibility ?? "public",
            });
            if (sent) KmhNotifications.Neutral("Posting auction…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Auction a full-state item (complex) by its treasury payload fingerprint - state preserved through escrow.
        public static bool TryPostPayload(string fingerprint, int qty, long startingBid, long minIncrement,
            long buyout, int hours, string visibility)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.AuctionPost, new
            {
                fingerprint    = fingerprint ?? "",
                qty,
                starting_bid   = startingBid,
                min_increment  = minIncrement,
                buyout_silver  = buyout,
                hours,
                visibility     = visibility ?? "public",
            });
            if (sent) KmhNotifications.Neutral("Posting auction (full state)…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TryBid(long auctionId, long amount)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.AuctionBid, new { auction_id = auctionId, amount });
            if (sent) KmhNotifications.Neutral($"Bidding {amount}…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TryCancel(long auctionId)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.AuctionCancel, new { auction_id = auctionId });
            if (sent) KmhNotifications.Positive("Cancel sent");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            AuctionSnapshot snap = env?.DataAs<AuctionSnapshot>();
            if (snap == null) { KmhLog.Warn("Auction snapshot had no parseable payload, ignoring"); return; }
            AuctionCache.Apply(snap);
        }
    }
}
