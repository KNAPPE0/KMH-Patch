using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    internal static class ItemLabels
    {
        private static readonly Dictionary<string, string> Cache
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public static string ResolveLabel(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return "?";

            if (defName.IndexOf(ItemKeys.Sep) >= 0) return ItemKeys.LabelForKey(defName);

            if (Cache.TryGetValue(defName, out string cached)) return cached;

            string resolved = ResolveUncached(defName);
            // Lock-free cache write; races only repeat the same lookup, not bad data.
            Cache[defName] = resolved;
            return resolved;
        }

        public static string ResolveStuffedLabel(string itemDefName, string stuffDefName)
        {
            if (string.IsNullOrEmpty(stuffDefName)) return ResolveLabel(itemDefName);
            return $"{ResolveLabel(stuffDefName)} {ResolveLabel(itemDefName)}";
        }

        private static string ResolveUncached(string defName)
        {
            try
            {
                ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (td != null && !string.IsNullOrEmpty(td.label)) return td.label;
            }
            catch
            {
                // DefDatabase can fail before game load finishes, so fall back to the raw defName.
            }
            return defName;
        }

        public static void DrawIcon(Rect rect, string defName) => KmhIconResolver.Draw(rect, defName);
    }
}
