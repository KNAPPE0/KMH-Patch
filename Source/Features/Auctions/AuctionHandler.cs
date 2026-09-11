using KMHPatch.Diagnostics;
using KMHPatch.Features.Auctions.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Auctions
{
    // Bids draw from the treasury server-side, so a bid sends only the amount and removes nothing locally.
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
            }, KmhOpId.For($"auction.post|{itemDefName}|{stuffDefName}|{qualityIndex}|{qty}|{startingBid}"));
            if (sent) KmhNotifications.Neutral("Posting auction…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        // By payload fingerprint, so a full-state item keeps its exact state through escrow.
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
            }, KmhOpId.For($"auction.post|{fingerprint}|{qty}|{startingBid}"));
            if (sent) KmhNotifications.Neutral("Posting auction (full state)…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryBid(long auctionId, long amount)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.AuctionBid, new { auction_id = auctionId, amount });
            if (sent) KmhNotifications.Neutral($"Bidding {amount}…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryCancel(long auctionId)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.AuctionCancel, new { auction_id = auctionId });
            if (sent) KmhNotifications.Positive("Cancel sent");
            else      KmhNotifications.NotConnected();
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
