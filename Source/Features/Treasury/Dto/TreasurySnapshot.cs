using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Treasury.Dto
{
    // JSON wire mirror of the treasury. Server resolves which treasury (guild or personal) from the caller's
    // authenticated identity - the client just asks for "the treasury that applies to me" and renders whatever
    // comes back.
    public class TreasurySnapshot
    {
        // Guild name, or "_personal:<username>". Used in the dialog title.
        [JsonProperty("owner_key")]            public string OwnerKey         { get; set; } = "";
        [JsonProperty("is_guild_owned")]       public bool   IsGuildOwned     { get; set; } = false;

        [JsonProperty("silver_balance")]       public int    SilverBalance    { get; set; } = 0;
        [JsonProperty("lifetime_silver_in")]   public long   LifetimeSilverIn { get; set; } = 0;
        [JsonProperty("lifetime_silver_out")]  public long   LifetimeSilverOut{ get; set; } = 0;

        // ThingDef defName → count.
        [JsonProperty("items")]                public Dictionary<string, int> Items
            { get; set; } = new Dictionary<string, int>();

        // Most recent first (server sends in chronological order - newest at end of list - the dialog reverses for
        // display)
        [JsonProperty("recent_transactions")]  public List<TreasuryTransaction> RecentTransactions
            { get; set; } = new List<TreasuryTransaction>();

        // Permissions - server computes per-caller. Lets the UI gate Deposit/ Withdraw buttons in future
        // mutation-capable ports (v1 is read-only so these are informational)
        [JsonProperty("can_deposit")]          public bool CanDeposit  { get; set; } = false;
        [JsonProperty("can_withdraw")]         public bool CanWithdraw { get; set; } = false;
    }

    // One entry in the treasury activity log.
    public class TreasuryTransaction
    {
        // Wire values stay snake_case strings (not int enum) so a log entry remains human-readable mid-debug and
        // resilient against enum renames on either side
        public const string KindDeposit            = "deposit";
        public const string KindWithdraw           = "withdraw";
        public const string KindSiteRewardSilver   = "site_reward_silver";
        public const string KindSiteRewardItem     = "site_reward_item";
        public const string KindMarketplaceSale    = "marketplace_sale";
        public const string KindMarketplaceTax     = "marketplace_tax";
        public const string KindMarketplaceRefund  = "marketplace_refund";

        [JsonProperty("utc_ticks")]      public long   UtcTicks    { get; set; } = 0;
        [JsonProperty("username")]       public string Username    { get; set; } = "";
        [JsonProperty("kind")]           public string Kind        { get; set; } = "";

        // Silver tx: amount; item tx: count.
        [JsonProperty("amount")]         public int    Amount      { get; set; } = 0;

        // Item tx only.
        [JsonProperty("item_def_name")]  public string ItemDefName { get; set; } = "";

        [JsonProperty("note")]           public string Note        { get; set; } = "";
    }
}
