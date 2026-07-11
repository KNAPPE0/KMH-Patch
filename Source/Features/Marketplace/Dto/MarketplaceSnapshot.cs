using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Marketplace.Dto
{
    // Wire DTO for marketplace snapshots; server stays authoritative.
    public class MarketplaceSnapshot
    {
        [JsonProperty("listings")]
        public List<MarketplaceListing> Listings { get; set; } = new List<MarketplaceListing>();

        [JsonProperty("house_silver_pool")]
        public long HouseSilverPool { get; set; } = 0;

        [JsonProperty("server_tax_percent")]
        public int ServerTaxPercent { get; set; } = 0;   // base marketplace tax before guild perk reduction

        [JsonProperty("lifetime_trades_completed")]
        public long LifetimeTradesCompleted { get; set; } = 0;

        [JsonProperty("lifetime_silver_traded")]
        public long LifetimeSilverTraded { get; set; } = 0;
    }

    public class MarketplaceListing
    {
        // Server id used for buy/cancel requests.
        [JsonProperty("id")]                  public long   Id              { get; set; } = 0;

        [JsonProperty("seller_username")]     public string SellerUsername  { get; set; } = "";

        // Empty means delivery goes to the seller's caravan.
        [JsonProperty("seller_treasury_key")] public string SellerTreasuryKey { get; set; } = "";

        [JsonProperty("item_def_name")]       public string ItemDefName     { get; set; } = "";

        [JsonProperty("remaining_qty")]       public int    RemainingQty    { get; set; } = 0;
        [JsonProperty("original_qty")]        public int    OriginalQty     { get; set; } = 0;
        [JsonProperty("unit_price_silver")]   public int    UnitPriceSilver { get; set; } = 0;

        [JsonProperty("listed_utc_ticks")]    public long   ListedUtcTicks  { get; set; } = 0;

        // 0 = never expires.
        [JsonProperty("expires_utc_ticks")]   public long   ExpiresUtcTicks { get; set; } = 0;

        // Server-created listing from site output; useful for future UI badges.
        [JsonProperty("is_auto_listing")]     public bool   IsAutoListing   { get; set; } = false;

        // 1=Awful..7=Legendary; 0 = no quality.
        [JsonProperty("quality_index")]       public int    QualityIndex    { get; set; } = 0;

        // Empty for non-stuffable items.
        [JsonProperty("stuff_def_name")]      public string StuffDefName    { get; set; } = "";

        // Server already filters visibility; client keeps this for badges and wire parity.
        [JsonProperty("visibility")]          public string Visibility      { get; set; } = "public";

        // Full-state escrow (blob stripped over wire); fingerprint + display note for complex-item listings.
        [JsonProperty("escrow_payloads")]     public List<KMHPatch.Items.KmhThingPayload> EscrowPayloads { get; set; }
        [JsonProperty("state_fingerprint")]   public string StateFingerprint { get; set; } = "";
        [JsonProperty("state_note")]          public string StateNote        { get; set; } = "";

        public int TotalAskingSilver(int qty) => UnitPriceSilver * (qty < 0 ? 0 : qty);
    }
}