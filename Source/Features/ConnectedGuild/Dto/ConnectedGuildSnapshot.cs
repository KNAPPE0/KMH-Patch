using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.ConnectedGuild.Dto
{
    // A guild has no player token, so the server carries Global's last-read balance, stamped with the read time.
    // Shown, never acted on: a withdrawal is authorised by Global's hold.
    public class ConnectedGuildSnapshot
    {
        [JsonProperty("available")] public bool   Available { get; set; }
        [JsonProperty("linked")]    public bool   Linked { get; set; }
        [JsonProperty("in_guild")]  public bool   InGuild { get; set; }
        [JsonProperty("guild")]     public string Guild { get; set; } = "";
        [JsonProperty("rank")]      public string Rank { get; set; } = "";

        [JsonProperty("may_move")]  public bool   MayMove { get; set; }
        [JsonProperty("may_admin")] public bool   MayAdmin { get; set; }
        // Why a button is disabled, in the guild's own words. Empty when it is not.
        [JsonProperty("move_why")]  public string MoveWhy { get; set; } = "";
        [JsonProperty("admin_why")] public string AdminWhy { get; set; } = "";

        [JsonProperty("link_id")]   public string LinkId { get; set; } = "";
        [JsonProperty("guild_id")]  public string GuildId { get; set; } = "";
        [JsonProperty("state")]     public string State { get; set; } = "";
        [JsonProperty("state_why")] public string StateWhy { get; set; } = "";

        [JsonProperty("settled")]          public long SettledSilver { get; set; }
        [JsonProperty("reserved")]         public long ReservedSilver { get; set; }
        [JsonProperty("available_silver")] public long AvailableSilver { get; set; }
        [JsonProperty("debt")]             public long Debt { get; set; }
        [JsonProperty("frozen")]           public bool Frozen { get; set; }
        // When the four numbers above were read from Global. Zero means never, which is not the same as zero silver.
        [JsonProperty("read")]             public long BalanceReadUtcTicks { get; set; }

        [JsonProperty("local_silver")] public long LocalSilver { get; set; }
        [JsonProperty("pending_in")]   public long PendingIn { get; set; }
        [JsonProperty("pending_out")]  public long PendingOut { get; set; }
        [JsonProperty("max_amount")]   public long MaxAmount { get; set; }
        [JsonProperty("revision")]     public long Revision { get; set; }

        [JsonProperty("operations")]
        public List<ConnectedGuildOperation> Operations { get; set; } = new List<ConnectedGuildOperation>();

        [JsonIgnore] public bool NeedsReproof => State == "ReproofRequired";
        [JsonIgnore] public bool Revoked      => State == "Revoked";
        // A balance may be read while re-proof is pending; only spending it is blocked.
        [JsonIgnore] public bool CanMoveValue => Linked && State == "Active";
    }

    public class ConnectedGuildOperation
    {
        [JsonProperty("id")]      public string Id { get; set; } = "";
        [JsonProperty("kind")]    public string Kind { get; set; } = "";
        [JsonProperty("amount")]  public long   Amount { get; set; }
        [JsonProperty("phase")]   public string Phase { get; set; } = "";
        [JsonProperty("open")]    public bool   Open { get; set; }
        // Which member started it, so a guild can see who moved its silver.
        [JsonProperty("who")]     public string Who { get; set; } = "";
        [JsonProperty("detail")]  public string Detail { get; set; } = "";
        [JsonProperty("updated")] public long   UpdatedUtcTicks { get; set; }

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
                    case "Delivering": return "Crediting the vault";
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
