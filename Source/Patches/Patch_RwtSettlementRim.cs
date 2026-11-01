using System;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Sites;
using RimWorld.Planet;
using UnityEngine;

namespace KMHPatch.Patches
{
    // RWT lets a player wear any faction's icon, so another player's settlement can pass for an NPC one; it gets the rim.
    internal static class Patch_RwtSettlementRim
    {
        internal static Type Settlement { get; private set; }
        private static Material _rim;
        private static Color _rimColor;

        public static void TryApply(Harmony harmony)
        {
            try
            {
                string ns = RwtCompat.ClientNsRoot;
                Settlement = RwtCompat.ResolveType(ns, "WO_Settlement", ns + ".WorldObjects.WO_Settlement");
                MethodInfo getter = AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.ExpandingMaterial));
                if (Settlement == null || getter == null)
                {
                    KmhLog.Info("Settlement rims: this RWT build has no player settlement object - nothing to outline.");
                    return;
                }
                // An RWT that draws its own border keeps it.
                if (AccessTools.DeclaredPropertyGetter(Settlement, nameof(WorldObject.ExpandingMaterial)) != null)
                {
                    KmhLog.Info("Settlement rims: RWT draws its own border on player settlements - left as it is.");
                    return;
                }
                harmony.Patch(getter, postfix: new HarmonyMethod(typeof(Patch_RwtSettlementRim)
                    .GetMethod(nameof(AddRim), BindingFlags.NonPublic | BindingFlags.Static)));
                KmhLog.Info("Settlement rims: other players' RWT settlements are outlined on the world map.");
            }
            catch (Exception ex) { KmhLog.Warn($"Settlement rim setup threw: {ex.Message}"); }
        }

        // RimWorld's own border for a settlement you hold a map on stays; so does any material RWT sets later.
        private static void AddRim(WorldObject __instance, ref Material __result)
        {
            if (__result != null || __instance == null || !Settlement.IsInstanceOfType(__instance) || !KmhMarkerColors.Enabled) return;
            if (__instance is MapParent parent && parent.HasMap) return;
            Color c = KmhMarkerColors.Theirs;
            if (_rim == null || _rimColor != c) { _rimColor = c; _rim = KmhMarkerRender.Halo(c); }
            __result = _rim;
        }
    }
}
