using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Catalog.Dto
{
    // One DTO per file, names matching the addon's copy: the contract check compares the two files field for field.
    public class ItemLabelsPush
    {
        [JsonProperty("labels")]
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

        // defName -> BaseMarketValue for server-side pricing; old clients omit it and the server treats it as 0.
        [JsonProperty("values")]
        public Dictionary<string, long> Values { get; set; } = new Dictionary<string, long>();

        // Lets the server consolidate treasury payloads stored before the per-payload mergeable flag existed.
        [JsonProperty("fungible")]
        public List<string> Fungible { get; set; } = new List<string>();

        // 1-based, and both omitted by an old client sending a single push.
        [JsonProperty("chunk_index")] public int ChunkIndex { get; set; } = 0;
        [JsonProperty("chunk_total")] public int ChunkTotal { get; set; } = 0;
    }
}
