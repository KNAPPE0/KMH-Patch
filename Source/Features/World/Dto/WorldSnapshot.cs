using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.World.Dto
{
    // Mirror of the addon-side DTO: property names stay byte-identical, and type tags ride as strings so renames never corrupt old data.
    public class WorldSnapshot
    {
        // Monotonic server stamp; one older than the applied snapshot is dropped, since two transports can deliver out of order.
        [JsonProperty("revision")] public long Revision { get; set; } = 0;
        [JsonProperty("events")]        public List<WorldEventDto>  Events       { get; set; } = new List<WorldEventDto>();
        [JsonProperty("server_quests")] public List<ServerQuestDto> ServerQuests { get; set; } = new List<ServerQuestDto>();
    }

    public class WorldEventDto
    {
        public const string TaxHoliday      = "tax_holiday";
        public const string MarketBoom      = "market_boom";
        public const string MarketCrash     = "market_crash";
        public const string ResourceShortage = "resource_shortage";
        public const string DoubleWorkerXp  = "double_worker_xp";
        public const string HouseStipend    = "house_stipend";
        public const string BountyTarget    = "bounty_target";
        public const string WorldWeather    = "world_weather";     // apply GameConditionDef Target to our maps while active

        [JsonProperty("id")]                public long   Id              { get; set; } = 0;
        [JsonProperty("type")]              public string Type            { get; set; } = "";
        [JsonProperty("title")]             public string Title           { get; set; } = "";
        [JsonProperty("description")]       public string Description     { get; set; } = "";
        [JsonProperty("magnitude")]         public double Magnitude       { get; set; } = 0;
        [JsonProperty("target")]            public string Target          { get; set; } = "";
        [JsonProperty("started_utc_ticks")] public long   StartedUtcTicks { get; set; } = 0;
        [JsonProperty("ends_utc_ticks")]    public long   EndsUtcTicks    { get; set; } = 0;
    }

    public class ServerQuestDto
    {
        public const string KindCooperative = "cooperative";
        public const string KindCompetitive = "competitive";

        public const string ObjDeliver = "deliver";
        public const string ObjHunt    = "hunt";
        public const string ObjBuild   = "build";

        public const string OpNone     = "";
        public const string OpAssault  = "assault";
        public const string OpCapture  = "capture";
        public const string OpSupply   = "supply";
        public const string OpRepair   = "repair";
        public const string OpReclaim  = "reclaim";
        public const string OpDefend   = "defend";

        public static readonly string[] AllOperationTypes =
            { OpAssault, OpCapture, OpSupply, OpRepair, OpReclaim, OpDefend };

        public const string SourceWorldDirector = "world_director";

        public const string ConsequenceNone             = "";
        public const string ConsequenceOutpostClaimable = "outpost_claimable";
        public const string ConsequenceOutpostReclaimed = "outpost_reclaimed";
        public const string ConsequenceOutpostCaptured  = "outpost_captured";

        public static readonly string[] AllConsequences =
            { ConsequenceOutpostClaimable, ConsequenceOutpostReclaimed, ConsequenceOutpostCaptured };

        public const string StateActive    = "active";
        public const string StateCompleted = "completed";
        public const string StateExpired   = "expired";

        [JsonProperty("id")]              public long   Id            { get; set; } = 0;
        [JsonProperty("kind")]            public string Kind          { get; set; } = KindCooperative;
        [JsonProperty("objective")]       public string Objective     { get; set; } = ObjDeliver;
        [JsonProperty("title")]           public string Title         { get; set; } = "";
        [JsonProperty("description")]     public string Description   { get; set; } = "";
        [JsonProperty("target_def_name")] public string TargetDefName { get; set; } = "";
        [JsonProperty("goal_qty")]        public int    GoalQty       { get; set; } = 0;
        [JsonProperty("progress_qty")]    public int    ProgressQty   { get; set; } = 0;
        [JsonProperty("reward_pool")]     public long   RewardPool    { get; set; } = 0;
        // Server-owned bookkeeping (pool-backed vs minted reward). Carried for DTO parity; the client doesn't use it.
        [JsonProperty("reserved_from_pool")] public long ReservedFromPool { get; set; } = 0;
        [JsonProperty("state")]           public string State         { get; set; } = StateActive;
        [JsonProperty("winner")]          public string Winner        { get; set; } = "";
        [JsonProperty("ends_utc_ticks")]  public long   EndsUtcTicks  { get; set; } = 0;

        // Frontier Operations: an ordinary server quest carries none of this.
        [JsonProperty("operation_type")]   public string OperationType   { get; set; } = OpNone;
        [JsonProperty("operation_source")] public string OperationSource { get; set; } = "";
        [JsonProperty("target_site_tile")] public int    TargetSiteTile  { get; set; } = -1;
        [JsonProperty("consequence")]      public string Consequence     { get; set; } = ConsequenceNone;
        [JsonProperty("window_ends_utc")]  public long   WindowEndsUtcTicks { get; set; } = 0;

        // Ties on quantity go to whoever helped first, so this is a rule rather than dictionary order.
        [JsonProperty("contributor_first_utc")] public Dictionary<string, long> ContributorFirstUtc { get; set; }
            = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        [JsonProperty("contributors")]    public Dictionary<string, int> Contributors
            { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}
