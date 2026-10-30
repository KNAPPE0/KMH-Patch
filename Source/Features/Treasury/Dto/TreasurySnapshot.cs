using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Treasury.Dto
{
    // JSON wire mirror; the server picks guild vs personal from the caller's authenticated identity.
    public class TreasurySnapshot
    {
        // Guild name, or "_personal:<username>".
        [JsonProperty("owner_key")]            public string OwnerKey         { get; set; } = "";
        [JsonProperty("is_guild_owned")]       public bool   IsGuildOwned     { get; set; } = false;

        [JsonProperty("silver_balance")]       public int    SilverBalance    { get; set; } = 0;
        [JsonProperty("lifetime_silver_in")]   public long   LifetimeSilverIn { get; set; } = 0;
        [JsonProperty("lifetime_silver_out")]  public long   LifetimeSilverOut{ get; set; } = 0;

        [JsonProperty("items")]                public Dictionary<string, int> Items
            { get; set; } = new Dictionary<string, int>();

        // ScribeXml is absent in snapshots (display metadata only); the full payload rides the withdraw grant.
        [JsonProperty("item_payloads")]        public List<KMHPatch.Items.KmhThingPayload> ItemPayloads
            { get; set; } = new List<KMHPatch.Items.KmhThingPayload>();

        [JsonProperty("recent_transactions")]  public List<TreasuryTransaction> RecentTransactions
            { get; set; } = new List<TreasuryTransaction>();

        [JsonProperty("pending_deposits")]     public List<PendingDeposit> PendingDeposits
            { get; set; } = new List<PendingDeposit>();

        // Server-only through PendingTakes (stripped before send); mirrored for wire-contract parity, never populated.
        [JsonProperty("recent_committed_txns")] public List<string> RecentCommittedTxns { get; set; }

        [JsonProperty("committed_deposits")] public List<PendingDeposit> CommittedDeposits { get; set; }

        [JsonProperty("last_save_generation")] public long LastSaveGeneration { get; set; }

        [JsonProperty("ledger_generations")] public Dictionary<string, long> LedgerGenerations { get; set; }

        [JsonProperty("pending_takes")] public List<PendingTake> PendingTakes { get; set; }

        [JsonProperty("can_deposit")]          public bool CanDeposit  { get; set; } = false;
        [JsonProperty("can_withdraw")]         public bool CanWithdraw { get; set; } = false;

        [JsonProperty("revision")]             public long Revision    { get; set; } = 0;
    }

    public class TreasuryTransaction
    {
        // snake_case strings, not an int enum, so log rows stay readable and survive enum renames on either side.
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

        [JsonProperty("item_def_name")]  public string ItemDefName { get; set; } = "";

        [JsonProperty("note")]           public string Note        { get; set; } = "";
    }

    public class PendingDeposit
    {
        // Guild donation pending: donor already debited, guild credited only on save-confirm.
        public const string KindGuildDonate = "guild_donate";

        public const string KindItem        = "item";
        public const string KindPayload     = "payload";
        public const string KindSilver      = "silver";
        // The server only ever stores these two - a reverted or timed-out deposit is removed, not marked.
        public const string StateCommitted  = "committed";
        public const string StatePending    = "pending";

        [JsonProperty("txn_id")]          public string TxnId          { get; set; } = "";
        [JsonProperty("username")]        public string Username       { get; set; } = "";
        [JsonProperty("created_utc")]     public long   CreatedUtcTicks{ get; set; } = 0;
        [JsonProperty("committed_utc")]   public long   CommittedUtcTicks { get; set; } = 0;   // server-side; parity only
        [JsonProperty("state")]           public string State          { get; set; } = StatePending;
        [JsonProperty("kind")]            public string Kind           { get; set; } = KindSilver;
        [JsonProperty("silver")]          public int    Silver         { get; set; } = 0;
        [JsonProperty("item_def_name")]   public string ItemDefName     { get; set; } = "";
        [JsonProperty("qty")]             public int    Qty            { get; set; } = 0;
        [JsonProperty("payloads")]        public List<KMHPatch.Items.KmhThingPayload> Payloads { get; set; } = new List<KMHPatch.Items.KmhThingPayload>();
        [JsonProperty("note")]            public string Note           { get; set; } = "";
        [JsonProperty("save_gen_at_open")] public long  SaveGenAtOpen  { get; set; } = 0;   // server-side only; present for DTO parity
        [JsonProperty("fee")]             public int    Fee            { get; set; } = 0;   // server-side only; present for DTO parity
        [JsonProperty("guild_name")]      public string GuildName      { get; set; } = "";
        [JsonProperty("guild_id")]        public string GuildIdentity  { get; set; } = "";  // server-side only; present for DTO parity
        [JsonProperty("ledger_id")]       public string LedgerId       { get; set; } = "";  // server-side only; present for DTO parity
        [JsonProperty("confirmed_gen")]   public long   ConfirmedGeneration { get; set; } = 0;   // server-side only; present for DTO parity
    }

    // Server-side only; present for DTO parity. The vault owns these until a transaction row names them.
    public class PendingTake
    {
        [JsonProperty("marker")]        public string TakeMarker   { get; set; } = "";
        [JsonProperty("refund_marker")] public string RefundMarker { get; set; } = "";
        [JsonProperty("username")]      public string Username     { get; set; } = "";
        [JsonProperty("taken_utc")]     public long   TakenUtcTicks{ get; set; } = 0;
        [JsonProperty("note")]          public string Note         { get; set; } = "";
        [JsonProperty("payloads")]      public List<KMHPatch.Items.KmhThingPayload> Payloads { get; set; } = new List<KMHPatch.Items.KmhThingPayload>();
    }
}
