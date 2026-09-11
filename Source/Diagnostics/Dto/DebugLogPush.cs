using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Diagnostics.Dto
{
    // Mirror of the addon DTO; a field changed here has to change there too.
    public class DebugLogPush
    {
        [JsonProperty("lines")] public List<string> Lines { get; set; } = new List<string>();

        // Without it, matching a server-side diagnostic to the local log means guessing from timestamps.
        [JsonProperty("session_id")] public string SessionId { get; set; } = "";
        [JsonProperty("seq")]        public long   Sequence  { get; set; } = 0;
    }
}
