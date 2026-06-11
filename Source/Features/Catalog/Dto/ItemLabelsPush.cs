using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Catalog.Dto
{
    // wire DTO for kmh.item_labels; property names must match the addon's ItemLabelsPush or the server gets an empty map
    //
    // Patch-side lives under .Features.Catalog rather than .Features .ItemLabels because the latter would shadow
    // the existing KMHPatch.UI.ItemLabels label-resolver class when nested Features files do
    // `ItemLabels.ResolveLabel(...)` - C# would resolve `ItemLabels` to a namespace first and fail
    public class ItemLabelsPush
    {
        [JsonProperty("labels")]
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    }
}
