using HarmonyLib;
using KMHPatch.Features.Wealth;
using Verse;

namespace KMHPatch.Patches
{
    // Only the storyteller path, so wealth parked in KMH cannot sidestep raid points while the Wealth tab still shows on-map value.
    [HarmonyPatch(typeof(Map), nameof(Map.PlayerWealthForStoryteller), MethodType.Getter)]
    internal static class Patch_Map_PlayerWealthForStoryteller_KmhWealth
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            try { __result += KmhWealthLedger.Total(); }
            catch { /* never let wealth injection break the storyteller's wealth read */ }
        }
    }
}
