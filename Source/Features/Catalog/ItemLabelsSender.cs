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
        // Entries per chunk - keeps each message well under the ~64KB router frame. Large modpacks send several
        // chunks; the server accumulates them (Apply is additive), so nothing is lost off the tail.
        private const int ChunkSize = 800;

        public static bool PushOnce()
        {
            if (!KmhDispatcher.IsKmhServer) return false;

            Dictionary<string, string> labels = BuildCatalog(out int discovered, out Dictionary<string, long> values);
            if (labels.Count == 0)
            {
                KmhLog.Warn("ItemLabels: no items resolved from DefDatabase, skipping push");
                return false;
            }

            int labelChunks = SendChunked(KmhProtocol.Kind.ItemLabels, labels, forValues: false);
            if (labelChunks <= 0) return false;
            KmhLog.Debug($"ItemLabels: pushed {labels.Count} labels in {labelChunks} chunk(s) (of {discovered} discovered).");

            if (values.Count > 0)
            {
                int vChunks = SendChunked(KmhProtocol.Kind.ItemValues, ToStr(values), forValues: true);
                KmhLog.Debug($"ItemLabels: pushed {values.Count} base market values in {vChunks} chunk(s).");
            }
            PushConditionDefs();
            return true;
        }

        // Split a dict into chunks and send each as one ItemLabelsPush with chunk_index/chunk_total metadata.
        private static int SendChunked(string kind, Dictionary<string, string> map, bool forValues)
        {
            List<KeyValuePair<string, string>> all = new List<KeyValuePair<string, string>>(map);
            int total = (all.Count + ChunkSize - 1) / ChunkSize;
            if (total <= 0) return 0;
            int sent = 0;
            for (int c = 0; c < total; c++)
            {
                ItemLabelsPush push = new ItemLabelsPush { ChunkIndex = c + 1, ChunkTotal = total };
                for (int i = c * ChunkSize; i < all.Count && i < (c + 1) * ChunkSize; i++)
                {
                    if (forValues) { if (long.TryParse(all[i].Value, out long v)) push.Values[all[i].Key] = v; }
                    else           push.Labels[all[i].Key] = all[i].Value;
                }
                if (KmhDispatcher.Send(kind, push)) sent++;
            }
            return sent;
        }

        private static Dictionary<string, string> ToStr(Dictionary<string, long> m)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, long> kv in m) d[kv.Key] = kv.Value.ToString();
            return d;
        }

        // GameConditionDefs (incl. modded) so the server's discovered-weather pool knows what this game can show.
        private static void PushConditionDefs()
        {
            try
            {
                Dictionary<string, string> conditions = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (GameConditionDef def in DefDatabase<GameConditionDef>.AllDefsListForReading)
                {
                    if (def == null || string.IsNullOrEmpty(def.defName)) continue;
                    conditions[def.defName] = string.IsNullOrEmpty(def.label) ? def.defName : def.LabelCap;
                }
                if (conditions.Count > 0)
                {
                    int n = SendChunked(KmhProtocol.Kind.ConditionDefs, conditions, forValues: false);
                    KmhLog.Debug($"ItemLabels: pushed {conditions.Count} game condition defs in {n} chunk(s).");
                }
            }
            catch (System.Exception ex) { KmhLog.Warn($"ItemLabels: condition-def push threw: {ex.Message}"); }
        }

        // Gather tradeable ThingDefs using the shared item filter; labels already handle lookup + fallback. NO cap -
        // chunking handles size, so the full modpack catalog reaches the server.
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
