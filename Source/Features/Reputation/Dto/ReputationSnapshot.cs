using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Reputation.Dto
{
    // Mirror of the addon's reputation snapshot - must match on the wire.
    public class ReputationSnapshot
    {
        [JsonProperty("entries")] public List<ReputationEntryDto> Entries { get; set; } = new List<ReputationEntryDto>();
    }

    public class ReputationEntryDto
    {
        [JsonProperty("username")] public string Username { get; set; } = "";
        [JsonProperty("score")]    public int    Score    { get; set; } = 0;
        [JsonProperty("tier")]     public string Tier     { get; set; } = "Neutral";
    }
}
