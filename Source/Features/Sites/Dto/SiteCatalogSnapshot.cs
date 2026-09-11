using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Sites.Dto
{
    // Mirror of the server SiteCatalogSnapshot; the picker renders this instead of the def database so it cannot drift.
    public class SiteCatalogSnapshot
    {
        [JsonProperty("entries")]           public List<SiteCatalogEntry> Entries { get; set; } = new List<SiteCatalogEntry>();
        [JsonProperty("tiers_enabled")]     public bool TiersEnabled    { get; set; } = true;
        [JsonProperty("max_allowed_tier")]  public int  MaxAllowedTier  { get; set; } = 3;
        [JsonProperty("tier4_enabled")]     public bool Tier4Enabled    { get; set; } = false;
        [JsonProperty("includes_blocked")]  public bool IncludesBlocked { get; set; } = false;
        [JsonProperty("catalog_sparse")]    public bool CatalogSparse      { get; set; } = false;
        // In display order; adding an archetype is a server-side change, the client never carries its own copy.
        [JsonProperty("archetypes")]        public List<SiteArchetypeInfo> Archetypes { get; set; } = new List<SiteArchetypeInfo>();
        // False = no classified catalog yet, so the picker must not pretend items are ineligible.
        [JsonProperty("archetypes_ready")]  public bool ArchetypesReady    { get; set; } = false;
        // What the server ADOPTED, not what was pushed - the only one of the two the client can check.
        [JsonProperty("catalog_fingerprint")] public string CatalogFingerprint { get; set; } = "";
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
        // Server-classified production family (plant/forestry/mineral/animal/crafted/construction/unknown).
        [JsonProperty("family")]        public string Family         { get; set; } = "unknown";
        // Comma-separated, computed server-side; Roadworks uses a predicate, so the client must never re-derive this.
        [JsonProperty("arch")]          public string AllowedArchetypes { get; set; } = "";
    }

    // Carries the numbers the server actually enforces, so the picker's preview cannot drift from the rule.
    public class SiteArchetypeInfo
    {
        [JsonProperty("id")]           public string Id          { get; set; } = "";
        [JsonProperty("name")]         public string DisplayName { get; set; } = "";
        [JsonProperty("desc")]         public string Description { get; set; } = "";
        [JsonProperty("skill")]        public string WorkerSkill { get; set; } = "";
        [JsonProperty("cost_mult")]    public double CostMultiplier { get; set; } = 1.0;
        [JsonProperty("perk")]         public string PerkText    { get; set; } = "";
        [JsonProperty("custom")]       public bool   IsCustom    { get; set; } = false;
        [JsonProperty("roadworks")]    public bool   UnlocksRoadworks { get; set; } = false;
    }

}
