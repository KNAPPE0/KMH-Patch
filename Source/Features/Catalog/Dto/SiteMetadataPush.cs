using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Catalog.Dto
{
    // Wire DTO for kmh.site_meta. Chunked like the label push - a big modpack is thousands of defs.
    public class SiteMetadataPush
    {
        [JsonProperty("meta")]        public List<Sites.Dto.SiteOutputMetadata> Meta { get; set; } = new List<Sites.Dto.SiteOutputMetadata>();
        [JsonProperty("chunk_index")] public int ChunkIndex { get; set; } = 0;
        [JsonProperty("chunk_total")] public int ChunkTotal { get; set; } = 0;
        // The server refuses to let a different client's facts overwrite an established catalog, and this is how it tells.
        [JsonProperty("fingerprint")] public string Fingerprint { get; set; } = "";
    }
}
