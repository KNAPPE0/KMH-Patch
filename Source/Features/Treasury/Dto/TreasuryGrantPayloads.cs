using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Treasury.Dto
{
    public class TreasuryGrantPayloads
    {
        [JsonProperty("kind")]     public string Kind { get; set; } = "";
        [JsonProperty("payloads")] public List<KMHPatch.Items.KmhThingPayload> Payloads { get; set; }
    }
}
