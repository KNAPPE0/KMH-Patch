using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Treasury.Dto
{
    // Payload grant from the server on a full-state withdraw (kmh.treasury.grant, kind="item_payloads").
    public class TreasuryGrantPayloads
    {
        [JsonProperty("kind")]     public string Kind { get; set; } = "";
        [JsonProperty("payloads")] public List<KMHPatch.Items.KmhThingPayload> Payloads { get; set; }
    }
}
