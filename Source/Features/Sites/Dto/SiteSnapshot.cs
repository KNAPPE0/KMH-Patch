using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Sites.Dto
{
    // Mirror of the addon's KMHServerAddon.Features.Sites.Dto - must stay byte-identical on the wire. A custom site
    // is a player-built node that produces a chosen item each cycle; workers boost output and earn XP
    public class SiteSnapshot
    {
        [JsonProperty("sites")]   public List<SiteEntry> Sites { get; set; } = new List<SiteEntry>();
        [JsonProperty("allow_custom_sites")] public bool AllowCustomSites { get; set; } = true;
        [JsonProperty("price_multiplier")]   public double PriceMultiplier  { get; set; } = 3.0;
        [JsonProperty("max_reward_amount")]  public int    MaxRewardAmount  { get; set; } = 50;
    }

    public class SiteEntry
    {
        public const string AccessGuildOnly = "guild_only";
        public const string AccessPublic    = "public";
        public const string AccessPrivate   = "private";

        public const string DestCaravan     = "caravan";
        public const string DestTreasury    = "treasury";
        public const string DestMarketplace = "marketplace";

        [JsonProperty("tile")]              public int    Tile             { get; set; } = -1;
        [JsonProperty("owner_username")]    public string OwnerUsername    { get; set; } = "";
        [JsonProperty("owner_guild")]       public string OwnerGuild       { get; set; } = "";

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

        [JsonProperty("last_reward_utc_ticks")] public long  LastRewardUtcTicks   { get; set; } = 0;
        [JsonProperty("total_silver_generated")] public double TotalSilverGenerated { get; set; } = 0;

        [JsonProperty("production_multiplier")] public double ProductionMultiplier { get; set; } = 0;
        [JsonProperty("effective_cycle_minutes")] public double EffectiveCycleMinutes { get; set; } = 0;
    }

    public class WorkerProgressDto
    {
        [JsonProperty("joined_utc_ticks")] public long   JoinedUtcTicks  { get; set; } = 0;
        [JsonProperty("cycles_completed")] public int    CyclesCompleted { get; set; } = 0;
        [JsonProperty("xp")]               public double Xp              { get; set; } = 0;
        [JsonProperty("base_skill_level")] public int    BaseSkillLevel  { get; set; } = 0;
        [JsonProperty("destination")]      public string Destination     { get; set; } = SiteEntry.DestTreasury;

        [JsonIgnore] public int CurrentLevel
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
                return Math.Max(BaseSkillLevel, xpLevel);
            }
        }
    }
}
