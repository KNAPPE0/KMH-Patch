using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Mail.Dto
{
    // Field names must stay byte-identical with the server DTO (KMH-Server-Addon Features/Mail/Dto/MailMessage.cs).
    public class MailMessage
    {
        [JsonProperty("id")]             public long   Id           { get; set; } = 0;
        [JsonProperty("from")]           public string From         { get; set; } = "";
        [JsonProperty("to")]             public string To           { get; set; } = "";
        [JsonProperty("subject")]        public string Subject      { get; set; } = "";
        [JsonProperty("body")]           public string Body         { get; set; } = "";
        [JsonProperty("sent_utc_ticks")] public long   SentUtcTicks { get; set; } = 0;
        [JsonProperty("read_utc_ticks")] public long   ReadUtcTicks { get; set; } = 0;

        // AttachState mirrors the server: 0 none, 1 escrowed, 2 claimed, 3 refunded, and only 1 offers Accept or Decline.
        [JsonProperty("attach_silver")]   public long   AttachedSilver { get; set; } = 0;
        [JsonProperty("attach_items")]    public Dictionary<string, int> AttachedItems { get; set; }
        [JsonProperty("attach_payloads")] public List<KMHPatch.Items.KmhThingPayload> AttachedPayloads { get; set; }
        [JsonProperty("attach_state")]    public int    AttachState    { get; set; } = 0;

        public bool HasItems          => AttachedItems != null && AttachedItems.Count > 0;
        public bool HasGear           => AttachedPayloads != null && AttachedPayloads.Count > 0;
        public bool IsUnread          => ReadUtcTicks == 0;
        public bool HasOpenAttachment => AttachState == 1 && (AttachedSilver > 0 || HasItems || HasGear);
    }
}
