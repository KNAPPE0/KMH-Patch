using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Catalog.Dto;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using Verse;

namespace KMHPatch.Features.Catalog
{
    // Sends the local item catalog once per session so the server can cache friendly names for Discord commands.
    internal static class ItemLabelsSender
    {
        // Keep the push under the 64KB router cap; chunked catalog sends can come later.
        private const int MaxEntries = 1200;

        public static bool PushOnce()
        {
            if (!KmhDispatcher.IsKmhServer) return false;

            Dictionary<string, string> labels = BuildCatalog(out int discovered, out Dictionary<string, long> values);
            if (labels.Count == 0)
            {
                KmhLog.Warn("ItemLabels: no items resolved from DefDatabase, skipping push");
                return false;
            }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.ItemLabels,
                new ItemLabelsPush { Labels = labels });
            if (sent)
            {
                KmhLog.Debug(
                    $"ItemLabels: pushed {labels.Count} labels to server" +
                    (discovered > labels.Count
                        ? $" (capped from {discovered} for size - server side won't have the tail)"
                        : ""));

                // Send market values separately so the label push stays under the 64KB envelope cap.
                if (values.Count > 0 && KmhDispatcher.Send(KmhProtocol.Kind.ItemValues, new ItemLabelsPush { Values = values }))
                    KmhLog.Debug($"ItemLabels: pushed {values.Count} base market values to server");
            }
            return sent;
        }

        // Gather tradeable ThingDefs using the shared item filter; labels already handle lookup + fallback.
        private static Dictionary<string, string> BuildCatalog(out int discovered, out Dictionary<string, long> values)
        {
            discovered = 0;
            Dictionary<string, string> result = new Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase);
            values = new Dictionary<string, long>(System.StringComparer.OrdinalIgnoreCase);
            try
            {
                Dictionary<string, int> pickable = ItemDefBrowser.AllPickableItems();
                discovered = pickable.Count;
                foreach (string defName in pickable.Keys)
                {
                    if (string.IsNullOrEmpty(defName)) continue;
                    string label = ItemLabels.ResolveLabel(defName);
                    // Only ship real labels; identity mappings add noise with no value.
                    if (string.IsNullOrEmpty(label)) continue;
                    if (string.Equals(label, defName, System.StringComparison.Ordinal)) continue;
                    result[defName] = label;
                    // Include RimWorld's canonical price when the def has a positive market value.
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def != null && def.BaseMarketValue > 0f)
                        values[defName] = (long)System.Math.Round(def.BaseMarketValue);
                    if (result.Count >= MaxEntries) break;
                }
            }
            catch (System.Exception ex)
            {
                KmhLog.Warn($"ItemLabels: catalog build threw: {ex.Message}");
            }
            return result;
        }
    }
}
