using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.LinkedAccounts.Dto
{
    // An empty or missing snapshot means no linkage is known, and the UI falls back to plain usernames.
    public class LinkedAccountsSnapshot
    {
        [JsonProperty("links")]
        public Dictionary<string, string> Links
            { get; set; } = new Dictionary<string, string>();
    }
}
