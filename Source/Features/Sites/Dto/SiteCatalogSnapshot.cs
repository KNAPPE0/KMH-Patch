using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Sites.Dto
{
    // Mirror of the server SiteCatalogSnapshot - the curated, server-classified list of what a site may produce. The
    // client picker renders this instead of the full def database, so it can't drift from the server tier rules or
    // feel like dev-mode item spawning.
    public class SiteCatalogSnapshot
    {
        [JsonProperty("entries")]           public List<SiteCatalogEntry> Entries { get; set; } = new List<SiteCatalogEntry>();
        [JsonProperty("tiers_enabled")]     public bool TiersEnabled    { get; set; } = true;
        [JsonProperty("max_allowed_tier")]  public int  MaxAllowedTier  { get; set; } = 3;
        [JsonProperty("tier4_enabled")]     public bool Tier4Enabled    { get; set; } = false;
        [JsonProperty("includes_blocked")]  public bool IncludesBlocked { get; set; } = false;
        [JsonProperty("catalog_sparse")]    public bool CatalogSparse   { get; set; } = false;
    }

    public class SiteCatalogEntry
    {
        [JsonProperty("def_name")]      public string DefName        { get; set; } = "";
        [JsonProperty("label")]         public string Label          { get; set; } = "";
        [JsonProperty("tier")]          public int    Tier           { get; set; } = 1;
        [JsonProperty("tier_name")]     public string TierName       { get; set; } = "";
        [JsonProperty("skill")]         public string RelevantSkill  { get; set; } = "";
        [JsonProperty("max_amount")]    public int    MaxAmount      { get; set; } = 1;
        [JsonProperty("est_cost")]      public int    EstBuildCost   { get; set; } = 0;
        [JsonProperty("est_cycle_min")] public int    EstCycleMinutes{ get; set; } = 0;
        [JsonProperty("allowed")]       public bool   Allowed        { get; set; } = true;
        [JsonProperty("block_reason")]  public string BlockReason    { get; set; } = "";
    }
}
