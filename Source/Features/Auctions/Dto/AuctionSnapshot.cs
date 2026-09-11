using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Auctions.Dto
{
    // Byte-identical to the addon's KMHServerAddon.Features.Auctions.Dto.
    public class AuctionSnapshot
    {
        // Server-stamped under its read lock; older revisions are dropped because transports reorder.
        [JsonProperty("revision")] public long Revision { get; set; } = 0;
        [JsonProperty("auctions")] public List<AuctionDto> Auctions { get; set; } = new List<AuctionDto>();
    }

    public class AuctionDto
    {
        [JsonProperty("id")]                  public long   Id                { get; set; } = 0;
        [JsonProperty("seller_username")]     public string SellerUsername    { get; set; } = "";
        [JsonProperty("seller_treasury_key")] public string SellerTreasuryKey { get; set; } = "";
        [JsonProperty("item_def_name")]       public string ItemDefName       { get; set; } = "";
        [JsonProperty("stuff_def_name")]      public string StuffDefName      { get; set; } = "";
        [JsonProperty("quality_index")]       public int    QualityIndex      { get; set; } = 0;
        [JsonProperty("qty")]                 public int    Qty               { get; set; } = 0;
        [JsonProperty("starting_bid")]        public long   StartingBid       { get; set; } = 0;
        [JsonProperty("min_increment")]       public long   MinIncrement      { get; set; } = 1;
        [JsonProperty("buyout_silver")]       public long   BuyoutSilver      { get; set; } = 0;
        [JsonProperty("current_bid")]         public long   CurrentBid        { get; set; } = 0;
        [JsonProperty("high_bidder")]         public string HighBidder        { get; set; } = "";
        [JsonProperty("bid_count")]           public int    BidCount          { get; set; } = 0;
        [JsonProperty("listed_utc_ticks")]    public long   ListedUtcTicks    { get; set; } = 0;
        [JsonProperty("ends_utc_ticks")]      public long   EndsUtcTicks      { get; set; } = 0;
        [JsonProperty("visibility")]          public string Visibility        { get; set; } = "public";

        // Server-only and stripped before send; mirrored here for wire-contract parity and never populated.
        [JsonProperty("escrow_payloads")]     public List<KMHPatch.Items.KmhThingPayload> EscrowPayloads { get; set; }
        [JsonProperty("state_fingerprint")]   public string StateFingerprint  { get; set; } = "";
        [JsonProperty("state_note")]          public string StateNote         { get; set; } = "";
    }
}
