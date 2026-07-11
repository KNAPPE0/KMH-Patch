using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Diagnostics.Dto
{
    // Wire DTO for kmh.debug.log - a batch of preformatted KMH log lines. Mirror of the addon DTO.
    public class DebugLogPush
    {
        [JsonProperty("lines")] public List<string> Lines { get; set; } = new List<string>();
    }
}
