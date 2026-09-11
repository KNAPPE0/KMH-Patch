using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Mail.Dto
{
    // Sent server-side as an anonymous object, so there is no matching DTO file for the contract check to compare.
    public class MailSnapshot
    {
        // Server-stamped under its read lock; older revisions are dropped because transports reorder.
        [JsonProperty("revision")] public long Revision { get; set; } = 0;
        [JsonProperty("messages")] public List<MailMessage> Messages { get; set; } = new List<MailMessage>();
        [JsonProperty("unread")]   public int               Unread   { get; set; } = 0;

        // My sent mail still holding an escrowed attachment the recipient hasn't opened - the set I can recall.
        [JsonProperty("outgoing")] public List<MailMessage> Outgoing { get; set; } = new List<MailMessage>();

        // Counted as my wealth via MailWealthSource, so parking goods in outgoing mail cannot shelter them.
        [JsonProperty("escrowed_out_silver")]        public long                    EscrowedOutSilver       { get; set; } = 0;
        [JsonProperty("escrowed_out_items")]         public Dictionary<string, int> EscrowedOutItems        { get; set; }
        [JsonProperty("escrowed_out_payload_value")] public long                    EscrowedOutPayloadValue { get; set; } = 0;
    }
}
