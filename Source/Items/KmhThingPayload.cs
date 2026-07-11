using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Items
{
    // Mirror of the addon's KMHServerAddon.Items.KmhThingPayload - same JSON property names + semantics. The client
    // fills this from a real Thing (KmhThingCapture) and rebuilds a Thing from it on withdraw/grant.
    public class KmhThingPayload
    {
        public const int CurrentSchema = 1;

        public const string FidelityFull     = "full";
        public const string FidelityMetadata = "metadata";
        public const string FidelityLegacy   = "legacy";

        [JsonProperty("schema_version")] public int    SchemaVersion { get; set; } = CurrentSchema;
        [JsonProperty("def_name")]       public string DefName       { get; set; } = "";
        [JsonProperty("stuff_def_name")] public string StuffDefName  { get; set; } = "";
        [JsonProperty("stack_count")]    public int    StackCount    { get; set; } = 1;

        [JsonProperty("hit_points")]     public int    HitPoints     { get; set; } = -1;
        [JsonProperty("max_hit_points")] public int    MaxHitPoints  { get; set; } = -1;
        [JsonProperty("quality")]        public int    Quality       { get; set; } = 0;
        [JsonProperty("tainted")]        public bool   Tainted       { get; set; } = false;

        [JsonProperty("scribe_xml")]     public string ScribeXml     { get; set; } = "";
        [JsonProperty("fidelity")]       public string Fidelity      { get; set; } = FidelityLegacy;

        [JsonProperty("display_label")]  public string DisplayLabel  { get; set; } = "";
        [JsonProperty("market_value")]   public long   MarketValue   { get; set; } = 0;
        [JsonProperty("fingerprint")]    public string Fingerprint   { get; set; } = "";
        [JsonProperty("legacy")]         public bool   Legacy        { get; set; } = false;
        [JsonProperty("warnings")]       public List<string> Warnings { get; set; } = new List<string>();
    }
}
