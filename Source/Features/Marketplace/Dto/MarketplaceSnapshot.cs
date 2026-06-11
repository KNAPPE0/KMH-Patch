using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Marketplace.Dto
{
    // JSON wire mirror of the marketplace + listings (snake_case names). Server is the source of truth - the client
    // never mutates a listing locally.
    public class MarketplaceSnapshot
    {
        [JsonProperty("listings")]
        public List<MarketplaceListing> Listings { get; set; } = new List<MarketplaceListing>();

        [JsonProperty("house_silver_pool")]
        public long HouseSilverPool { get; set; } = 0;

        [JsonProperty("lifetime_trades_completed")]
        public long LifetimeTradesCompleted { get; set; } = 0;

        [JsonProperty("lifetime_silver_traded")]
        public long LifetimeSilverTraded { get; set; } = 0;
    }

    // One open listing in the marketplace.
    public class MarketplaceListing
    {
        // Server-assigned monotonic id. Used as the key for cancel / buy mutation envelopes when those land
        [JsonProperty("id")]                  public long   Id              { get; set; } = 0;

        [JsonProperty("seller_username")]     public string SellerUsername  { get; set; } = "";

        // Empty → deliveries go to seller's caravan; otherwise to their treasury at this key
        [JsonProperty("seller_treasury_key")] public string SellerTreasuryKey { get; set; } = "";

        [JsonProperty("item_def_name")]       public string ItemDefName     { get; set; } = "";

        [JsonProperty("remaining_qty")]       public int    RemainingQty    { get; set; } = 0;
        [JsonProperty("original_qty")]        public int    OriginalQty     { get; set; } = 0;
        [JsonProperty("unit_price_silver")]   public int    UnitPriceSilver { get; set; } = 0;

        [JsonProperty("listed_utc_ticks")]    public long   ListedUtcTicks  { get; set; } = 0;
        // 0 = never expires.
        [JsonProperty("expires_utc_ticks")]   public long   ExpiresUtcTicks { get; set; } = 0;

        // Auto-listings come from sites with RewardDestination=Marketplace (server-side concept). Useful to badge
        // separately in the UI later
        [JsonProperty("is_auto_listing")]     public bool   IsAutoListing   { get; set; } = false;

        // 1=Awful..7=Legendary; 0 = no quality (bulk resources).
        [JsonProperty("quality_index")]       public int    QualityIndex    { get; set; } = 0;

        // Empty for non-stuffable items (Steel, Wood, etc).
        [JsonProperty("stuff_def_name")]      public string StuffDefName    { get; set; } = "";

        public int TotalAskingSilver(int qty) => UnitPriceSilver * (qty < 0 ? 0 : qty);
    }
}
