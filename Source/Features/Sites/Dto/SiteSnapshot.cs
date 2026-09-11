using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Sites.Dto
{
    // Mirror of the addon's KMHServerAddon.Features.Sites.Dto - must stay byte-identical on the wire.
    public class SiteSnapshot
    {
        // Monotonic; an older revision is dropped because two transports can deliver out of order.
        [JsonProperty("revision")] public long Revision { get; set; } = 0;
        [JsonProperty("sites")]   public List<SiteEntry> Sites { get; set; } = new List<SiteEntry>();
        [JsonProperty("allow_custom_sites")] public bool AllowCustomSites { get; set; } = true;
        [JsonProperty("price_multiplier")]   public double PriceMultiplier  { get; set; } = 3.0;
        [JsonProperty("max_reward_amount")]  public int    MaxRewardAmount  { get; set; } = 50;

        [JsonProperty("building_cost")]        public int BuildingCostSilver  { get; set; } = 750;
        [JsonProperty("repair_per_point")]     public int RepairCostPerPoint  { get; set; } = 25;
        [JsonProperty("storage_per_building")] public int StoragePerBuilding  { get; set; } = 100;
        [JsonProperty("max_storage")]          public int MaxStorageUnits     { get; set; } = 300;
        [JsonProperty("workers_per_housing")]  public int WorkersPerHousing   { get; set; } = 2;
        [JsonProperty("max_housing_bonus")]    public int MaxHousingBonus     { get; set; } = 4;
        [JsonProperty("production_bonus_pct")] public int ProductionBonusPct  { get; set; } = 10;
        [JsonProperty("max_production_pct")]   public int MaxProductionPct    { get; set; } = 30;
    }

    // Echoes the request back so a late reply can be discarded instead of shown against newer input.
    public class SiteBuildQuote
    {
        [JsonProperty("ok")]            public bool   Ok           { get; set; }
        [JsonProperty("reason")]        public string Reason       { get; set; } = "";
        [JsonProperty("def")]           public string ItemDefName  { get; set; } = "";
        [JsonProperty("amount")]        public int    Amount       { get; set; }
        [JsonProperty("archetype")]     public string Archetype    { get; set; } = "";
        [JsonProperty("family")]        public string Family       { get; set; } = "";
        [JsonProperty("cost")]          public int    Cost         { get; set; }
        [JsonProperty("value")]         public float  MarketValuePerUnit { get; set; }
        [JsonProperty("cost_mult")]     public double CostMultiplier     { get; set; } = 1.0;
        [JsonProperty("cycle_minutes")] public int    CycleMinutes { get; set; }
        [JsonProperty("max_amount")]    public int    MaxAmount    { get; set; }
        [JsonProperty("balance")]       public long   Balance      { get; set; }
        [JsonProperty("affordable")]    public bool   Affordable   { get; set; }
    }

    public class SiteEntry
    {
        public const string OwnerPlayer    = "player";
        public const string OwnerGuildKind = "guild";
        public const string OwnerSystem    = "system";
        public const string OwnerNeutral   = "neutral";

        public const string AccessGuildOnly = "guild_only";
        public const string AccessPublic    = "public";
        public const string AccessPrivate   = "private";

        public const string DestCaravan     = "caravan";
        public const string DestTreasury    = "treasury";
        public const string DestMarketplace = "marketplace";
        public const string DestStorage     = "storage";

        public const string TemplateNone      = "";
        public const string TemplateRuins     = "ruins";
        public const string TemplateResource  = "resource";
        public const string TemplateDepot     = "depot";
        public const string TemplateFortified = "fortified";
        public const string TemplateRelay     = "relay";

        public static readonly string[] AllTemplates =
            { TemplateRuins, TemplateResource, TemplateDepot, TemplateFortified, TemplateRelay };

        public const string OutpostNone      = "";
        public const string OutpostDerelict  = "derelict";
        public const string OutpostHostile   = "hostile";
        public const string OutpostDefeated  = "defeated";
        public const string OutpostClaimable = "claimable";
        public const string OutpostCaptured  = "captured";
        public const string OutpostDormant   = "dormant";

        public static readonly string[] AllOutpostStates =
            { OutpostDerelict, OutpostHostile, OutpostDefeated, OutpostClaimable, OutpostCaptured, OutpostDormant };

        public const string ArchetypeCustom    = "custom";
        public const string ArchetypeFarmland  = "farmland";
        public const string ArchetypeQuarry    = "quarry";
        public const string ArchetypeWoodland  = "woodland";
        public const string ArchetypeRoadworks = "roadworks";
        // Additive in v1.3.0: a site saved by an older build simply never carries it.
        public const string ArchetypeRanch     = "ranch";

        [JsonProperty("tile")]              public int    Tile             { get; set; } = -1;
        [JsonProperty("owner_username")]    public string OwnerUsername    { get; set; } = "";
        [JsonProperty("owner_guild")]       public string OwnerGuild       { get; set; } = "";

        // The ONLY authority discriminator; each field below is authoritative for exactly one kind. Absent = player.
        [JsonProperty("owner_kind")]        public string OwnerKind       { get; set; } = OwnerPlayer;
        // Authoritative for owner_kind == guild. Never taken from a client; the server derives it.
        [JsonProperty("controlling_guild")] public string ControllingGuild { get; set; } = "";
        // Optional system/neutral controller identity, so hostile locations are not all one generic owner.
        [JsonProperty("controller_faction")] public string ControllerFaction { get; set; } = "";
        // Presentation only. Never determines permission.
        [JsonProperty("site_name")]         public string SiteName        { get; set; } = "";

        // Frontier Operations, layered over Archetype; blank = an ordinary Site, which keeps existing data untouched.
        [JsonProperty("outpost_template")]  public string OutpostTemplate { get; set; } = TemplateNone;
        // Where it sits in the outpost lifecycle. Blank = not an outpost.
        [JsonProperty("outpost_state")]     public string OutpostState    { get; set; } = OutpostNone;
        [JsonProperty("established_utc")]   public long   EstablishedUtcTicks { get; set; } = 0;
        // When a claim window closes. 0 = not claimable.
        [JsonProperty("claim_window_ends_utc")] public long ClaimWindowEndsUtcTicks { get; set; } = 0;
        // The operation that established or resolved this location, for history.
        [JsonProperty("origin_operation_id")] public long OriginOperationId { get; set; } = 0;
        // Who took it, kept so captured infrastructure does not read like something built from scratch.
        [JsonProperty("captured_by")]       public string CapturedBy    { get; set; } = "";
        // Kept here because the operation that decided it is pruned long before the claim window closes.
        [JsonProperty("claim_eligible_username")] public string ClaimEligibleUsername { get; set; } = "";
        // Caller-scoped: whether THIS caller may claim it right now. Decided by the server, always.
        [JsonProperty("can_claim")]         public bool   CanClaim      { get; set; } = false;



        // Defaults match the server's, so an older snapshot that omits these reads as healthy and unbuilt, not damaged.
        [JsonProperty("archetype")]         public string Archetype       { get; set; } = "";
        [JsonProperty("stability")]         public int    Stability       { get; set; } = 100;
        [JsonProperty("buildings")]         public List<SiteBuilding> Buildings { get; set; } = new List<SiteBuilding>();

        // Output held at the site awaiting collection. The owner's value, so it counts as off-map wealth.
        [JsonProperty("stored_items")]      public Dictionary<string, int> StoredItems { get; set; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        [JsonProperty("item_def_name")]     public string ItemDefName      { get; set; } = "";
        [JsonProperty("base_amount")]       public int    BaseAmountPerCycle { get; set; } = 1;
        [JsonProperty("market_value")]      public float  MarketValuePerUnit { get; set; } = 0f;
        [JsonProperty("base_cycle_ms")]     public double BaseCycleTimeMs  { get; set; } = 1800000;

        [JsonProperty("access_mode")]       public string AccessMode       { get; set; } = AccessGuildOnly;
        [JsonProperty("owner_tax_percent")] public int    OwnerTaxPercent  { get; set; } = 10;

        [JsonProperty("workers")]           public List<string> Workers    { get; set; } = new List<string>();
        [JsonProperty("max_workers")]       public int    MaxWorkers       { get; set; } = 5;
        [JsonProperty("worker_progress")]   public Dictionary<string, WorkerProgressDto> WorkerProgress
            { get; set; } = new Dictionary<string, WorkerProgressDto>(StringComparer.OrdinalIgnoreCase);

        [JsonProperty("owner_destination")] public string OwnerRewardDestination { get; set; } = DestTreasury;
        [JsonProperty("marketplace_unit_price")] public int MarketplaceUnitPrice { get; set; } = 1;
        [JsonProperty("relevant_skill")]    public string RelevantSkillDef { get; set; } = "Crafting";

        // Caps default to 0 = "not reported" (a real cap is always >= 1.0), so the client can infer them from OutputTier.
        [JsonProperty("output_tier")]       public int    OutputTier            { get; set; } = 1;
        [JsonProperty("tier_max_speed")]    public double TierMaxSpeedMultiplier { get; set; } = 0;
        [JsonProperty("tier_max_output")]   public double TierMaxOutputMultiplier { get; set; } = 0;
        [JsonProperty("blocked_output")]    public bool   BlockedOutput         { get; set; } = false;

        [JsonProperty("last_reward_utc_ticks")] public long  LastRewardUtcTicks   { get; set; } = 0;
        [JsonProperty("total_silver_generated")] public double TotalSilverGenerated { get; set; } = 0;

        [JsonProperty("production_multiplier")] public double ProductionMultiplier { get; set; } = 0;
        // 0 = paused/unknown (server sanitizes - never Infinity/NaN/huge).
        [JsonProperty("effective_cycle_minutes")] public double EffectiveCycleMinutes { get; set; } = 0;
        [JsonProperty("is_producing")]      public bool   IsProducing { get; set; } = false;
        [JsonProperty("paused_reason")]     public string PausedReason { get; set; } = "";
    }

    // Mirror of the server's placed building. Display only - the server decides what may be built and what it does.
    public class SiteBuilding
    {
        public const string KindProduction = "production";
        public const string KindHousing    = "housing";
        public const string KindStorage    = "storage";
        public const string KindLogistics  = "logistics";
        public const string KindDefense    = "defense";

        public const string StateOperational = "operational";
        public const string StateDamaged     = "damaged";
        public const string StateRuined      = "ruined";

        [JsonProperty("kind")]  public string Kind  { get; set; } = "";
        [JsonProperty("level")] public int    Level { get; set; } = 1;
        [JsonProperty("state")] public string State { get; set; } = StateOperational;
    }

    public class WorkerProgressDto
    {
        [JsonProperty("joined_utc_ticks")] public long   JoinedUtcTicks  { get; set; } = 0;
        [JsonProperty("cycles_completed")] public int    CyclesCompleted { get; set; } = 0;
        [JsonProperty("xp")]               public double Xp              { get; set; } = 0;
        [JsonProperty("base_skill_level")] public int    BaseSkillLevel  { get; set; } = 0;
        [JsonProperty("destination")]      public string Destination     { get; set; } = SiteEntry.DestTreasury;

        // The colonist doing the work (advisory; empty = legacy account-level worker).
        [JsonProperty("pawn_name")]         public string PawnName          { get; set; } = "";
        [JsonProperty("pawn_load_id")]      public int    PawnLoadId        { get; set; } = -1;
        [JsonProperty("last_validated_utc")] public long  LastValidatedUtc  { get; set; } = 0;

        // Legacy = old account worker, disabled until a real pawn is assigned; BlockedReason explains any block.
        [JsonProperty("legacy")]            public bool   Legacy            { get; set; } = false;
        [JsonProperty("blocked_reason")]    public string BlockedReason     { get; set; } = "";

        // Mirrors the server: earned XP alone drives real work; the reported pawn skill is shown, never counted.
        [JsonIgnore] public int EarnedLevel
        {
            get
            {
                int xpLevel = 0;
                double remaining = Xp;
                while (xpLevel < 20)
                {
                    double need = 1000 + xpLevel * 1000;
                    if (remaining < need) break;
                    remaining -= need;
                    xpLevel++;
                }
                return xpLevel;
            }
        }

        // What the player is shown: their pawn's skill, or their earned level once it overtakes it.
        [JsonIgnore] public int CurrentLevel => Math.Max(BaseSkillLevel, EarnedLevel);
    }
}
