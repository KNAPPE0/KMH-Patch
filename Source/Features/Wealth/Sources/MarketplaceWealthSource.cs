using System;
using KMHPatch.Features.Marketplace;
using KMHPatch.Features.Marketplace.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.Wealth.Sources
{
    // Goods parked in the player's own listing escrow, at market value: asking price is deliberately NOT used, so mispricing cannot move raid pressure.
    internal sealed class MarketplaceWealthSource : IKmhWealthSource
    {
        public string Name => "Marketplace";

        public float SilverValue()
        {
            MarketplaceSnapshot s = MarketplaceCache.Snapshot;
            string me = KmhSession.Me;
            if (s?.Listings == null || string.IsNullOrEmpty(me)) return 0f;

            float v = 0f;
            foreach (MarketplaceListing l in s.Listings)
            {
                if (l == null || !string.Equals(l.SellerUsername, me, StringComparison.OrdinalIgnoreCase)) continue;

                float itemValue = KmhWealthValue.Payloads(l.EscrowPayloads);
                if (itemValue <= 0f && l.RemainingQty > 0)   // no payload detail: value from the def
                    itemValue = KmhWealthValue.CompactDef(l.ItemDefName, l.RemainingQty);

                v += itemValue;
            }
            return v;
        }
    }
}
