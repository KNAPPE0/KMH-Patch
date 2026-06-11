using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.LinkedAccounts.Dto
{
    // In-game username -> Discord display name map. Server pushes this on handshake and again whenever a
    // link/unlink happens, so the cache stays fresh without polling
    //
    // Wire shape is a dict for compactness (no per-entry "username" / "discord_name" property names repeated).
    // Empty / missing snapshot means "no Discord linkage info available" - UI falls back to plain username
    // rendering
    public class LinkedAccountsSnapshot
    {
        [JsonProperty("links")]
        public Dictionary<string, string> Links
            { get; set; } = new Dictionary<string, string>();
    }
}
