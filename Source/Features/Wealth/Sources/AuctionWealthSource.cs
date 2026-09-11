using System;
using KMHPatch.Features.Auctions;
using KMHPatch.Features.Auctions.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.Wealth.Sources
{
    // Both escrow sides: commit moves the seller's item and the bidder's silver OUT of the treasury, so nothing else counts them; valued at market value, never the asking price.
    internal sealed class AuctionWealthSource : IKmhWealthSource
    {
        public string Name => "Auctions";

        public float SilverValue()
        {
            AuctionSnapshot s = AuctionCache.Snapshot;
            string me = KmhSession.Me;
            if (s?.Auctions == null || string.IsNullOrEmpty(me)) return 0f;

            float v = 0f;
            foreach (AuctionDto a in s.Auctions)
            {
                if (a == null) continue;

                if (string.Equals(a.SellerUsername, me, StringComparison.OrdinalIgnoreCase))
                    v += SellerEscrowValue(a);

                // My winning bid is held in escrow until I'm outbid or the auction settles, so it is still my silver.
                if (a.CurrentBid > 0 && string.Equals(a.HighBidder, me, StringComparison.OrdinalIgnoreCase))
                    v += a.CurrentBid;
            }
            return v;
        }

        private static float SellerEscrowValue(AuctionDto a)
        {
            float v = KmhWealthValue.Payloads(a.EscrowPayloads);
            if (v <= 0f && a.Qty > 0)   // no payload detail: fall back to the def's value
                v = KmhWealthValue.CompactDef(a.ItemDefName, a.Qty);
            return v;
        }
    }
}
