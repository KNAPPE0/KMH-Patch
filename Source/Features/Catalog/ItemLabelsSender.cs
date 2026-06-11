using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Catalog.Dto;
using KMHPatch.SubProtocol;
using KMHPatch.UI;

namespace KMHPatch.Features.Catalog
{
    // Builds and sends the local DefDatabase item catalog to the server at handshake completion. Server caches the
    // union across all reporting clients so Discord-side market commands can show friendly labels ("packaged
    // survival meal" instead of "MealSurvivalPack") and accept friendly-name input ("plasteel" / "power armor")
    //
    // Send-once-per-session: the catalog doesn't change after RimWorld finishes loading. Re-sending on every
    // handshake (e.g., reconnect) is fine - server's Apply is idempotent for unchanged entries (cheap dirty check;
    // SaveToDisk only fires on real changes)
    //
    // Size cap: server router enforces 64KB / envelope. We cap input at ~1200 entries (estimated ~55 bytes each =
    // ~66KB raw) and log if we
    // have to truncate. Heavily-modded clients can lose tail entries;
    // chunked push is a follow-up if anyone hits that.
    internal static class ItemLabelsSender
    {
        // Conservative upper bound - leaves headroom under the 64KB envelope cap. Each entry averages ~55 bytes
        // serialized ("MealSurvivalPack":"packaged survival meal",) so 1200 * 55 = ~66KB raw which lands well
        // inside the 64KB cap after JSON overhead trimming
        private const int MaxEntries = 1200;

        public static bool PushOnce()
        {
            if (!KmhDispatcher.IsKmhServer) return false;

            Dictionary<string, string> labels = BuildCatalog(out int discovered);
            if (labels.Count == 0)
            {
                KmhLog.Warn("ItemLabels: no items resolved from DefDatabase, skipping push");
                return false;
            }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.ItemLabels,
                new ItemLabelsPush { Labels = labels });
            if (sent)
            {
                KmhLog.Info(
                    $"ItemLabels: pushed {labels.Count} labels to server" +
                    (discovered > labels.Count
                        ? $" (capped from {discovered} for size - server side won't have the tail)"
                        : ""));
            }
            return sent;
        }

        // Gathers tradeable ThingDef items via the existing ItemDefBrowser filter (skips chunks / corpses /
        // structures / debug-only items). The label comes from ItemLabels.ResolveLabel which already handles the
        // DefDatabase lookup + fallback
        private static Dictionary<string, string> BuildCatalog(out int discovered)
        {
            discovered = 0;
            Dictionary<string, string> result = new Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase);
            try
            {
                Dictionary<string, int> pickable = ItemDefBrowser.AllPickableItems();
                discovered = pickable.Count;
                foreach (string defName in pickable.Keys)
                {
                    if (string.IsNullOrEmpty(defName)) continue;
                    string label = ItemLabels.ResolveLabel(defName);
                    // ResolveLabel returns the defName on miss - only ship entries where we actually have a label
                    // distinct from the defName (no value in shipping identity mappings)
                    if (string.IsNullOrEmpty(label)) continue;
                    if (string.Equals(label, defName, System.StringComparison.Ordinal)) continue;
                    result[defName] = label;
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
