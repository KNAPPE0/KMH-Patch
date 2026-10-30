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

        internal static int CachedCountForTest => Cache.Count;

        public static string ResolveLabel(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return "?";

            if (defName.IndexOf(ItemKeys.Sep) >= 0) return ItemKeys.LabelForKey(defName);

            if (Cache.TryGetValue(defName, out string cached)) return cached;

            string resolved = ResolveUncached(defName);
            // A name no def has is another player's text: drawn inert and never kept, or each new one grew the cache.
            if (resolved == null) return KmhDisplayText.Inert(defName, 64);
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
                if (td != null) return string.IsNullOrEmpty(td.label) ? defName : td.label;
            }
            catch
            {
                // DefDatabase can fail before game load finishes; the caller shows the name inert and retries later.
            }
            return null;
        }

        public static void DrawIcon(Rect rect, string defName) => KmhIconResolver.Draw(rect, defName);
    }
}
