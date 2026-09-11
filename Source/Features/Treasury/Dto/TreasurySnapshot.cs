using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Treasury.Dto
{
    // JSON wire mirror; the server picks guild vs personal from the caller's authenticated identity.
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

        // ScribeXml is absent in snapshots (display metadata only); the full payload rides the withdraw grant.
        [JsonProperty("item_payloads")]        public List<KMHPatch.Items.KmhThingPayload> ItemPayloads
            { get; set; } = new List<KMHPatch.Items.KmhThingPayload>();

        // Server sends oldest-first; the dialog reverses for display.
        [JsonProperty("recent_transactions")]  public List<TreasuryTransaction> RecentTransactions
            { get; set; } = new List<TreasuryTransaction>();

        // Deposits held pending durable local-save confirmation - shown as "pending until saved", NOT spendable.
        [JsonProperty("pending_deposits")]     public List<PendingDeposit> PendingDeposits
            { get; set; } = new List<PendingDeposit>();

        // Server-only dedup ring (stripped before send); mirrored here for wire-contract parity, never populated.
        [JsonProperty("recent_committed_txns")] public List<string> RecentCommittedTxns { get; set; }

        // Server-only undo record for save rollback; mirrored for parity, never populated.
        [JsonProperty("committed_deposits")] public List<PendingDeposit> CommittedDeposits { get; set; }

        // Server-only save-generation watermark; mirrored for parity, never populated.
        [JsonProperty("last_save_generation")] public long LastSaveGeneration { get; set; }

        // Server-only payloads out of the vault with no transaction row yet; mirrored for parity, never populated.
        [JsonProperty("pending_takes")] public List<PendingTake> PendingTakes { get; set; }

        // Computed per-caller by the server; the dialog gates its Deposit/Withdraw buttons on these.
        [JsonProperty("can_deposit")]          public bool CanDeposit  { get; set; } = false;
        [JsonProperty("can_withdraw")]         public bool CanWithdraw { get; set; } = false;

        // Tracked per OwnerKey: personal and guild snapshots share a kind, so one counter would make each drop the other.
        [JsonProperty("revision")]             public long Revision    { get; set; } = 0;
    }

    // One entry in the treasury activity log.
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

        // Item tx only.
        [JsonProperty("item_def_name")]  public string ItemDefName { get; set; } = "";

        [JsonProperty("note")]           public string Note        { get; set; } = "";
    }

    // Mirror of the server PendingDeposit - a deposit whose local goods-removal isn't yet confirmed durably saved.
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
        [JsonProperty("guild_name")]      public string GuildName      { get; set; } = "";  // KindGuildDonate target
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
