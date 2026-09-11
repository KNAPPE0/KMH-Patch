using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Chat.Dto
{
    // Client-only wrappers: the server sends anonymous objects, so the contract check only compares ChatMessage.
    public class ChatSnapshot
    {
        [JsonProperty("channel")]  public string            Channel  { get; set; } = "";
        [JsonProperty("messages")] public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public class ChatMessagePush
    {
        [JsonProperty("message")] public ChatMessage Message { get; set; }
    }

    // server -> client kmh.chat.moderation { blocked, blocking_enabled } (my block list).
    public class ChatModerationSnapshot
    {
        [JsonProperty("blocked")]          public List<string> Blocked         { get; set; } = new List<string>();
        [JsonProperty("blocking_enabled")] public bool         BlockingEnabled { get; set; } = true;
    }
}
