using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Frontier.Dto
{
    // What the server asks: derive a candidate tile from this nonce, skipping these. Mirror of the server DTO.
    public class PlacementRequest
    {
        [JsonProperty("token_id")]      public string TokenId  { get; set; } = "";
        [JsonProperty("nonce")]         public long   Nonce    { get; set; } = 0;
        [JsonProperty("template")]      public string Template { get; set; } = "";
        [JsonProperty("exclude_tiles")] public List<int> ExcludeTiles { get; set; } = new List<int>();
        [JsonProperty("expires_utc")]   public long   ExpiresUtcTicks { get; set; } = 0;
    }
}
