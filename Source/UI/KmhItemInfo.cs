using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // RimWorld's REAL info card ("i" button) for KMH item rows, so players can inspect an item's stats before
    // depositing/withdrawing/buying/selling/bidding/fulfilling. Works from a composed key or def+stuff names -
    // no live Thing needed (server-side items only exist as defs + KMH payload metadata on this side).
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
