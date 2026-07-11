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

        // Simple/legacy compact items (def|stuff|quality key → count).
        [JsonProperty("items")]                public Dictionary<string, int> Items
            { get; set; } = new Dictionary<string, int>();

        // State-preserving complex items. ScribeXml is absent in snapshots (metadata only for display); the full
        // payload rides the grant on withdraw.
        [JsonProperty("item_payloads")]        public List<KMHPatch.Items.KmhThingPayload> ItemPayloads
            { get; set; } = new List<KMHPatch.Items.KmhThingPayload>();

        // Most recent first (server sends in chronological order - newest at end of list - the dialog reverses for
        // display)
        [JsonProperty("recent_transactions")]  public List<TreasuryTransaction> RecentTransactions
            { get; set; } = new List<TreasuryTransaction>();

        // Deposits held pending durable local-save confirmation - shown as "pending until saved", NOT spendable.
        [JsonProperty("pending_deposits")]     public List<PendingDeposit> PendingDeposits
            { get; set; } = new List<PendingDeposit>();

        // Server-only dedup ring (stripped before send); mirrored here for wire-contract parity, never populated.
        [JsonProperty("recent_committed_txns")] public List<string> RecentCommittedTxns { get; set; }

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

    // Mirror of the server PendingDeposit - a deposit whose local goods-removal isn't yet confirmed durably saved.
    public class PendingDeposit
    {
        // Guild donation pending: donor already debited, guild credited only on save-confirm.
        public const string KindGuildDonate = "guild_donate";

        [JsonProperty("txn_id")]          public string TxnId          { get; set; } = "";
        [JsonProperty("username")]        public string Username       { get; set; } = "";
        [JsonProperty("created_utc")]     public long   CreatedUtcTicks{ get; set; } = 0;
        [JsonProperty("state")]           public string State          { get; set; } = "pending";
        [JsonProperty("kind")]            public string Kind           { get; set; } = "silver";
        [JsonProperty("silver")]          public int    Silver         { get; set; } = 0;
        [JsonProperty("item_def_name")]   public string ItemDefName     { get; set; } = "";
        [JsonProperty("qty")]             public int    Qty            { get; set; } = 0;
        [JsonProperty("payloads")]        public List<KMHPatch.Items.KmhThingPayload> Payloads { get; set; } = new List<KMHPatch.Items.KmhThingPayload>();
        [JsonProperty("note")]            public string Note           { get; set; } = "";
        [JsonProperty("fee")]             public int    Fee            { get; set; } = 0;   // server-side only; present for DTO parity
        [JsonProperty("guild_name")]      public string GuildName      { get; set; } = "";  // KindGuildDonate target
    }
}
