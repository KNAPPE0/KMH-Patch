using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.ConnectedTreasury.Dto
{
    // No federated balance here: the client reads it from Global with its own token, so no server can misreport it.
    public class ConnectedSnapshot
    {
        [JsonProperty("available")]   public bool Available { get; set; }
        [JsonProperty("linked")]      public bool Linked { get; set; }
        [JsonProperty("pending_in")]  public long PendingIn { get; set; }
        [JsonProperty("pending_out")] public long PendingOut { get; set; }
        [JsonProperty("max_amount")]  public long MaxAmount { get; set; }
        [JsonProperty("revision")]    public long Revision { get; set; }

        [JsonProperty("operations")]
        public List<ConnectedOperation> Operations { get; set; } = new List<ConnectedOperation>();
    }

    public class ConnectedOperation
    {
        [JsonProperty("id")]      public string Id { get; set; } = "";
        [JsonProperty("kind")]    public string Kind { get; set; } = "";
        [JsonProperty("amount")]  public long Amount { get; set; }
        [JsonProperty("phase")]   public string Phase { get; set; } = "";
        [JsonProperty("open")]    public bool Open { get; set; }
        [JsonProperty("detail")]  public string Detail { get; set; } = "";
        [JsonProperty("updated")] public long UpdatedUtcTicks { get; set; }

        [JsonIgnore] public bool IsDeposit => Kind == "Deposit";

        // A phase this build does not recognise still displays, as itself, rather than as nothing.
        [JsonIgnore]
        public string Status
        {
            get
            {
                switch (Phase)
                {
                    case "Opening":
                    case "Reserving":  return "Starting";
                    case "Escrowed":   return "Sending to Colony Nexus Global";
                    case "Credited":   return "Credited";
                    case "Reserved":   return "Reserved";
                    case "Delivering": return "Delivering";
                    case "Delivered":  return "Delivered";
                    case "Settled":    return "Finalized";
                    case "Cancelled":  return "Failed";
                    default:           return Phase;
                }
            }
        }

        // An open operation carrying a reason is being retried, not stalled for ever.
        [JsonIgnore] public bool Retrying => Open && !string.IsNullOrEmpty(Detail);
    }
}
