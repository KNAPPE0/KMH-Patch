using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Catalog.Dto
{
    // Wire DTO for kmh.item_labels; names must match the addon's ItemLabelsPush or the server reads an empty map.
    // Kept under Features.Catalog to avoid shadowing KMHPatch.UI.ItemLabels in nested feature files.
    public class ItemLabelsPush
    {
        [JsonProperty("labels")]
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

        // defName -> BaseMarketValue for server-side pricing; old clients omit it and the server treats it as 0.
        [JsonProperty("values")]
        public Dictionary<string, long> Values { get; set; } = new Dictionary<string, long>();

        // Chunking for large modpacks (thousands of defs): each message is one chunk; the server accumulates them
        // (Apply is additive). 1-based index; total lets the server log completion. Old clients omit both (single push).
        [JsonProperty("chunk_index")] public int ChunkIndex { get; set; } = 0;
        [JsonProperty("chunk_total")] public int ChunkTotal { get; set; } = 0;
    }
}
