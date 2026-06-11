using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // ThingDef -> human label resolver for KMH dialogs that receive raw defNames from the server (Treasury items,
    // Marketplace listings, Quest delivery targets)
    //
    // Wire format carries defNames because they're stable across language packs and don't depend on the receiver's
    // loaded mods having the same localization. The receiver looks up the local DefDatabase to render a label the
    // player can actually read
    //
    // Resolution fallback: a defName not in DefDatabase (mod missing, or the server using a mod the client lacks)
    // returns the raw defName so the player still sees something identifiable
    //
    // Cache: lookups are memoized - same defName is resolved hundreds of times per redraw across multiple open
    // dialogs. Cache is process- scoped and never cleared (defs don't change after game load)
    internal static class ItemLabels
    {
        private static readonly Dictionary<string, string> Cache
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        // Returns the lowercase label ("packaged survival meal" for ThingDef "MealSurvivalPack"). Empty/null
        // defName -> "?"
        public static string ResolveLabel(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return "?";

            if (Cache.TryGetValue(defName, out string cached)) return cached;

            string resolved = ResolveUncached(defName);
            // Lock-free write - race produces at-worst-redundant resolution, never wrong data, since multiple
            // threads computing the same defName -> same label
            Cache[defName] = resolved;
            return resolved;
        }

        // Stuffable items: combine stuff label + item label ("plasteel" + "knife" -> "plasteel knife"). Falls back
        // to plain item label when stuffDefName is empty
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
                // DefDatabase access can throw if called before game load finishes. Fall through to the defName
                // fallback
            }
            return defName;
        }

        // Draw a small ThingDef icon into rect - used as a leading visual anchor in Treasury items / Marketplace
        // listings / Quest delivery rows. Silently no-ops when the def can't be resolved (mod missing,
        // pre-game-load, etc.) so the surrounding row layout stays stable
        //
        // The vanilla ThingDef.uiIcon may be tinted via uiIconColor - we honor that (e.g. apparel rendered in its
        // dye color)
        public static void DrawIcon(Rect rect, string defName)
        {
            if (string.IsNullOrEmpty(defName)) return;
            ThingDef td;
            try { td = DefDatabase<ThingDef>.GetNamedSilentFail(defName); }
            catch { return; }
            if (td?.uiIcon == null) return;

            Color prev   = GUI.color;
            Color tint   = td.uiIconColor;
            GUI.color    = tint == default ? Color.white : tint;
            Widgets.DrawTextureFitted(rect, td.uiIcon, 1f);
            GUI.color    = prev;
        }
    }
}
