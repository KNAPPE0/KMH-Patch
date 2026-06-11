using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Guilds.Dto
{
    // JSON wire mirror of the server's guild composite (file + members + perks + settings + alliances), snake_case
    // names.
    //
    // Server sends one snapshot per caller - InGuild=false means the caller is not currently in any guild, in which
    // case Guild is null and the dialog renders the "not in a guild" state.
    public class GuildSnapshotEnvelope
    {
        [JsonProperty("in_guild")]
        public bool InGuild { get; set; } = false;

        [JsonProperty("guild")]
        public GuildSnapshot Guild { get; set; }
    }

    public class GuildSnapshot
    {
        [JsonProperty("name")]      public string Name { get; set; } = "";
        [JsonProperty("motd")]      public string Motd { get; set; } = "";

        [JsonProperty("members")]   public List<GuildMemberDto> Members
            { get; set; } = new List<GuildMemberDto>();

        [JsonProperty("perks")]     public GuildPerksDto    Perks    { get; set; } = new GuildPerksDto();
        [JsonProperty("settings")]  public GuildSettingsDto Settings { get; set; } = new GuildSettingsDto();

        // Other guild name -> relationship value. Values are constants below.
        [JsonProperty("relationships")]
        public Dictionary<string, string> Relationships
            { get; set; } = new Dictionary<string, string>();

        [JsonProperty("open_join")]       public bool OpenJoin { get; set; } = false;
        [JsonProperty("pending_invites")] public List<string> PendingInvites { get; set; } = new List<string>();

        // Relationship values (snake_case strings, same convention as Quest state/kind - readable mid-debug,
        // survives enum renames)
        public const string RelationNone            = "none";
        public const string RelationAlliedRequested = "allied_requested";
        public const string RelationAllied          = "allied";
        public const string RelationHostile         = "hostile";
    }

    public class GuildMemberDto
    {
        // Ranks as snake_case strings on the wire.
        public const string RankMember    = "member";
        public const string RankModerator = "moderator";
        public const string RankOfficer   = "officer";
        public const string RankAdmin     = "admin";

        [JsonProperty("username")]            public string Username           { get; set; } = "";
        [JsonProperty("rank")]                public string Rank               { get; set; } = RankMember;

        [JsonProperty("silver_contributed")]  public long   SilverContributed  { get; set; } = 0;
        [JsonProperty("items_contributed")]   public long   ItemsContributed   { get; set; } = 0;
        [JsonProperty("quests_completed")]    public int    QuestsCompleted    { get; set; } = 0;
        [JsonProperty("joined_utc_ticks")]    public long   JoinedUtcTicks     { get; set; } = 0;
        [JsonProperty("withdrawn_today")]     public long   WithdrawnTodaySilver { get; set; } = 0;
        [JsonProperty("withdraw_day_ticks")]  public long   WithdrawDayStartUtc  { get; set; } = 0;
    }

    public class GuildPerksDto
    {
        // Per-perk max level cap.
        public const int MaxLevel = 3;

        [JsonProperty("site_max_workers_bonus_level")]    public int SiteMaxWorkersBonusLevel    { get; set; } = 0;
        [JsonProperty("marketplace_tax_reduction_level")] public int MarketplaceTaxReductionLevel { get; set; } = 0;
        [JsonProperty("worker_xp_bonus_level")]           public int WorkerXpBonusLevel          { get; set; } = 0;
        [JsonProperty("custom_site_cost_discount_level")] public int CustomSiteCostDiscountLevel { get; set; } = 0;
    }

    public class GuildSettingsDto
    {
        [JsonProperty("site_reward_silver_tax_percent")]  public int SiteRewardSilverTaxPercent { get; set; } = 0;
        [JsonProperty("marketplace_sale_tax_percent")]    public int MarketplaceSaleTaxPercent  { get; set; } = 0;

        // -1 = unlimited; 0 = withdraw disabled for that rank.
        [JsonProperty("member_daily_withdraw_cap")]       public int MemberDailyWithdrawCap    { get; set; } = 0;
        [JsonProperty("officer_daily_withdraw_cap")]      public int OfficerDailyWithdrawCap   { get; set; } = 0;
        [JsonProperty("moderator_daily_withdraw_cap")]    public int ModeratorDailyWithdrawCap { get; set; } = 0;
        [JsonProperty("admin_daily_withdraw_cap")]        public int AdminDailyWithdrawCap     { get; set; } = -1;

        [JsonProperty("default_listings_guild_only")]     public bool DefaultListingsGuildOnly { get; set; } = false;
    }
}
