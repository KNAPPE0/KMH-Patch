using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Works without a live Thing, because a server-side item exists here only as a def plus payload metadata.
    internal static class KmhItemInfo
    {
        public const float Size = 24f;

        public static void ButtonForKey(float x, float y, string key)
        {
            ItemKeys.Split(key, out string defName, out string stuffName, out _);
            ButtonForDef(x, y, defName, stuffName);
        }

        public static void ButtonForDef(float x, float y, string defName, string stuffName = null)
        {
            if (string.IsNullOrEmpty(defName)) return;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            ThingDef stuff = string.IsNullOrEmpty(stuffName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(stuffName);
            try { Widgets.InfoCardButton(x, y, def, stuff); }
            catch { /* a modded def with broken stats must not break the row */ }
        }
    }
}
