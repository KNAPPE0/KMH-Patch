using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Catalog.Dto;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using Verse;

namespace KMHPatch.Features.Catalog
{
    internal static class ItemLabelsSender
    {
        // Keeps each message well under the router frame; the server's Apply is additive, so nothing is lost off the tail.
        private const int ChunkSize = 800;

        // Shared with every other catalog pusher, so one of them cannot quietly lose the re-push throttle.
        internal const string GuardKind = "item-labels";

        public static bool PushOnce()
        {
            if (!KmhDispatcher.IsKmhServer) return false;

            string endpoint = CatalogPushGuard.CurrentEndpoint();
            if (CatalogPushGuard.AlreadySent(GuardKind, endpoint, out int agoSeconds))
            {
                KmhLog.Debug($"ItemLabels: catalog already sent to {endpoint} {agoSeconds}s ago - skipping re-push.");
                return true;   // report success so the session's push guard latches and won't retry over chat
            }

            Dictionary<string, string> labels = BuildCatalog(out int discovered, out Dictionary<string, long> values, out HashSet<string> fungible);
            if (labels.Count == 0)
            {
                KmhLog.Warn("ItemLabels: no items resolved from DefDatabase, skipping push");
                return false;
            }

            if (!SendChunkedComplete(KmhProtocol.Kind.ItemLabels, labels, forValues: false, fungible)) return false;
            KmhLog.Debug($"ItemLabels: pushed {labels.Count} labels (of {discovered} discovered).");

            // Values decide site pricing on the server, so a half-sent set is worse than none - it must not latch.
            if (values.Count > 0 && !SendChunkedComplete(KmhProtocol.Kind.ItemValues, ToStr(values), forValues: true))
                return false;
            if (!PushConditionDefs()) return false;

            CatalogPushGuard.MarkSent(GuardKind, endpoint);   // only after a COMPLETE push, so a partial one retries
            return true;
        }

        // True only when EVERY chunk went: "at least one chunk" latched a half-Unknown catalog as complete.
        private static bool SendChunkedComplete(string kind, Dictionary<string, string> map, bool forValues,
                                                HashSet<string> fungibleSet = null)
        {
            int total = (map.Count + ChunkSize - 1) / ChunkSize;
            int sent = SendChunked(kind, map, forValues, fungibleSet);
            if (sent == total) return true;
            KmhLog.Warn($"ItemLabels: only {sent} of {total} '{kind}' chunk(s) reached the server - the catalog is "
                      + "incomplete and will be sent again rather than treated as pushed.");
            return false;
        }

        private static int SendChunked(string kind, Dictionary<string, string> map, bool forValues, HashSet<string> fungibleSet = null)
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
                    if (fungibleSet != null && fungibleSet.Contains(all[i].Key)) push.Fungible.Add(all[i].Key);
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
        private static bool PushConditionDefs()
        {
            try
            {
                Dictionary<string, string> conditions = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (GameConditionDef def in DefDatabase<GameConditionDef>.AllDefsListForReading)
                {
                    if (def == null || string.IsNullOrEmpty(def.defName)) continue;
                    conditions[def.defName] = string.IsNullOrEmpty(def.label) ? def.defName : def.LabelCap;
                }
                if (conditions.Count == 0) return true;
                if (!SendChunkedComplete(KmhProtocol.Kind.ConditionDefs, conditions, forValues: false)) return false;
                KmhLog.Debug($"ItemLabels: pushed {conditions.Count} game condition defs.");
                return true;
            }
            catch (System.Exception ex) { KmhLog.Warn($"ItemLabels: condition-def push threw: {ex.Message}"); return false; }
        }

        // Deliberately uncapped: chunking handles size, so the full modpack catalog reaches the server.
        private static Dictionary<string, string> BuildCatalog(out int discovered, out Dictionary<string, long> values, out HashSet<string> fungible)
        {
            discovered = 0;
            Dictionary<string, string> result = new Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase);
            values = new Dictionary<string, long>(System.StringComparer.OrdinalIgnoreCase);
            fungible = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
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
                    // Vouch fungibility so the server can consolidate legacy (pre-mergeable-flag) treasury payloads.
                    if (def != null && Items.KmhThingCapture.IsFungible(def)) fungible.Add(defName);
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
