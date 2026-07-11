using KMHPatch.Items;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Single place item icons are resolved + drawn. A safe item whose icon failed gets a category fallback (never the
    // red-X BadTex); a missing/unsafe def draws nothing. ItemLabels.DrawIcon and every row delegate here.
    internal static class KmhIconResolver
    {
        public static Texture2D IconFor(string key) => IconForDef(DefOf(key));

        public static void Draw(Rect rect, string key)
        {
            ThingDef td = DefOf(key);
            Texture2D icon = IconForDef(td);
            if (icon == null) return;
            Color prev = GUI.color;
            Color tint = td?.uiIconColor ?? Color.white;
            GUI.color  = tint == default ? Color.white : tint;
            Widgets.DrawTextureFitted(rect, icon, 1f);
            GUI.color  = prev;
        }

        // Keys may be composed (def|stuff|quality); resolve the base def's icon.
        private static ThingDef DefOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            ItemKeys.Split(key, out string defName, out _, out _);
            try { return DefDatabase<ThingDef>.GetNamedSilentFail(defName); } catch { return null; }
        }

        private static Texture2D IconForDef(ThingDef td)
        {
            if (td == null) return null;
            Texture2D icon = KmhItemSafety.SafeIcon(td);
            return icon == BaseContent.BadTex ? null : icon;
        }
    }
}
