using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.PlayerStats.Dto
{
    // One row of the lifetime player leaderboard.Packets.PlayerLeaderboardEntry
    // field-for-field, but is our own type with snake_case JSON wire names - independent of any RWT/KMH-server
    // type. The future server addon will serialize an identical shape
    //
    // Defaults are sensible "no data" values so a malformed envelope from the server can't produce a runtime
    // null-deref in the dialog
    public class PlayerLeaderboardEntry
    {
        [JsonProperty("username")]             public string Username             { get; set; } = "";
        [JsonProperty("guild_name")]           public string GuildName            { get; set; } = "";
        [JsonProperty("is_linked_to_discord")] public bool   IsLinkedToDiscord    { get; set; } = false;
        [JsonProperty("first_seen_utc_ticks")] public long   FirstSeenUtcTicks    { get; set; } = 0;

        [JsonProperty("silver_donated")]       public long   SilverDonated        { get; set; } = 0;
        [JsonProperty("sales_earned")]         public long   SalesEarned          { get; set; } = 0;
        [JsonProperty("purchases_spent")]      public long   PurchasesSpent       { get; set; } = 0;
        [JsonProperty("quests_completed")]     public int    QuestsCompleted      { get; set; } = 0;
        [JsonProperty("quests_posted")]        public int    QuestsPosted         { get; set; } = 0;
        [JsonProperty("marketplace_sales")]    public int    MarketplaceSales     { get; set; } = 0;
        [JsonProperty("sites_built")]          public int    SitesBuilt           { get; set; } = 0;
        [JsonProperty("sites_raided")]         public int    SitesRaided          { get; set; } = 0;
        [JsonProperty("worker_xp")]            public long   WorkerXp             { get; set; } = 0;
        [JsonProperty("economy_score")]        public long   EconomyScore         { get; set; } = 0;
    }

    // Wrapper for the "kmh.player_stats.snapshot" envelope payload.
    public class PlayerStatsSnapshot
    {
        [JsonProperty("entries")]
        public List<PlayerLeaderboardEntry> Entries { get; set; } = new List<PlayerLeaderboardEntry>();
    }
}
