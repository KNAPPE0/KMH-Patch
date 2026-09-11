using Newtonsoft.Json;

namespace KMHPatch.Features.Chat.Dto
{
    // Field names must stay byte-identical with the server DTO (KMH-Server-Addon Features/Chat/Dto/ChatMessage.cs).
    public class ChatMessage
    {
        [JsonProperty("id")]             public long   Id           { get; set; } = 0;
        [JsonProperty("channel")]        public string Channel      { get; set; } = "";
        [JsonProperty("from")]           public string FromUsername { get; set; } = "";
        [JsonProperty("body")]           public string Body         { get; set; } = "";
        [JsonProperty("sent_utc_ticks")] public long   SentUtcTicks { get; set; } = 0;
        [JsonProperty("origin")]         public string Origin       { get; set; } = "ingame";
        [JsonProperty("verified")]       public bool   SenderVerified { get; set; }
        // Server-vetted host, not "fetch this": the client waits for a player click because fetching reveals their IP.
        [JsonProperty("image")]          public string ImageUrl      { get; set; } = "";
        [JsonProperty("is_video")]       public bool   IsVideo       { get; set; }
        // Server-converted copy for formats this client cannot decode; ImageUrl stays populated as the fallback.
        [JsonProperty("media_id")]       public string MediaId       { get; set; } = "";
        // Opaque re-fetch handle: Discord signs CDN urls for 24h but history keeps messages for 48.
        [JsonProperty("media_ref")]      public string MediaRef      { get; set; } = "";

        public bool FromDiscord => Origin == "discord";

        // The name is a self-chosen display string, not a player; old servers omit the flag and correctly read false.
        public bool UnverifiedSender => FromDiscord && !SenderVerified;
    }
}
