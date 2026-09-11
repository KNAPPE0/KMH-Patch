using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Guilds.Dto
{
    // InGuild=false means Guild is null and the dialog renders the "not in a guild" state.
    public class GuildSnapshotEnvelope
    {
        // Server-stamped under its read lock; older revisions are dropped because transports reorder.
        [JsonProperty("revision")] public long Revision { get; set; } = 0;
        [JsonProperty("in_guild")]
        public bool InGuild { get; set; } = false;

        [JsonProperty("guild")]
        public GuildSnapshot Guild { get; set; }

        // Standing invites FOR this player (guildless users only) - lets the client offer accept/decline.
        [JsonProperty("my_invites")] public List<GuildInviteDto> MyInvites { get; set; } = new List<GuildInviteDto>();
    }

    // One standing invite as the invitee sees it.
    public class GuildInviteDto
    {
        [JsonProperty("guild_name")]        public string GuildName       { get; set; } = "";
        [JsonProperty("inviter")]           public string Inviter         { get; set; } = "";
        [JsonProperty("created_utc_ticks")] public long   CreatedUtcTicks { get; set; } = 0;
        [JsonProperty("members")]           public int    Members         { get; set; } = 0;
    }

    // One row of the invite picker: a known, guildless player an officer can invite.
    public class InvitablePlayerDto
    {
        [JsonProperty("username")] public string Username { get; set; } = "";
        [JsonProperty("online")]   public bool   Online   { get; set; } = false;
    }

    public class GuildInvitablesSnapshot
    {
        [JsonProperty("players")] public List<InvitablePlayerDto> Players { get; set; } = new List<InvitablePlayerDto>();
    }

    public class GuildInviteMetaDto
    {
        [JsonProperty("inviter")]           public string Inviter         { get; set; } = "";
        [JsonProperty("created_utc_ticks")] public long   CreatedUtcTicks { get; set; } = 0;
    }

    // A guild's physical hall. null / HasHall=false = no hall (default + compat for pre-P8 guilds).
    public class GuildHallDto
    {
        [JsonProperty("has_hall")]          public bool   HasHall         { get; set; } = false;
        [JsonProperty("tile")]              public int    Tile            { get; set; } = -1;
        [JsonProperty("leader")]            public string Leader          { get; set; } = "";
        [JsonProperty("radius_tiles")]      public int    RadiusTiles     { get; set; } = 0;
        [JsonProperty("created_utc_ticks")] public long   CreatedUtcTicks { get; set; } = 0;
    }

    public class GuildSnapshot
    {
        [JsonProperty("name")]      public string Name { get; set; } = "";
        [JsonProperty("motd")]      public string Motd { get; set; } = "";

        [JsonProperty("members")]   public List<GuildMemberDto> Members
            { get; set; } = new List<GuildMemberDto>();

        [JsonProperty("perks")]     public GuildPerksDto    Perks    { get; set; } = new GuildPerksDto();
        [JsonProperty("settings")]  public GuildSettingsDto Settings { get; set; } = new GuildSettingsDto();
        [JsonProperty("guild_silver")]           public long GuildSilver { get; set; } = 0;   // silver-only guild vault balance
        [JsonProperty("guild_treasury_enabled")] public bool GuildTreasuryEnabled { get; set; } = true;
        // Donations awaiting a donor's save-confirm - shown in the Hall but never spendable (excluded from guild_silver).
        [JsonProperty("pending_donations_silver")] public long PendingDonationsSilver { get; set; } = 0;

        // Other guild name -> relationship value. Values are constants below.
        [JsonProperty("relationships")]
        public Dictionary<string, string> Relationships
            { get; set; } = new Dictionary<string, string>();

        [JsonProperty("open_join")]       public bool OpenJoin { get; set; } = false;
        [JsonProperty("pending_invites")] public List<string> PendingInvites { get; set; } = new List<string>();

        // Who issued each pending invite and when (keyed by invitee, additive beside pending_invites).
        [JsonProperty("invite_meta")]     public Dictionary<string, GuildInviteMetaDto> InviteMeta { get; set; }
            = new Dictionary<string, GuildInviteMetaDto>(System.StringComparer.OrdinalIgnoreCase);

        // Optional physical Guild Hall. null = no hall.
        [JsonProperty("hall")]            public GuildHallDto Hall { get; set; }

        // Strings rather than an enum, so a rename cannot silently change the wire value.
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
        public const string RankOwner     = "owner";   // exactly one per guild; only ownership transfer assigns it

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
