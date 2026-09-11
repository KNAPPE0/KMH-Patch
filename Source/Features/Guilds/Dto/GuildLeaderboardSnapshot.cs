using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Guilds.Dto
{
    // Mirrors the addon's GuildLeaderboardSnapshot, same JSON names and fields.
    public class GuildLeaderboardSnapshot
    {
        [JsonProperty("guilds")]
        public List<GuildLeaderboardEntry> Guilds { get; set; } = new List<GuildLeaderboardEntry>();
    }

    public class GuildLeaderboardEntry
    {
        [JsonProperty("name")]            public string Name           { get; set; } = "";
        [JsonProperty("member_count")]    public int    MemberCount    { get; set; } = 0;
        [JsonProperty("treasury_silver")] public long   TreasurySilver { get; set; } = 0;
        [JsonProperty("open_join")]       public bool   OpenJoin       { get; set; } = false;
    }
}
