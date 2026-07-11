using HarmonyLib;
using Verse;

namespace KMHPatch.Patches
{
    // Drives KMH world-marker reconciliation every frame the world exists - including the fresh-start landing-site
    // selection page and the paused world view, where WorldComponentTick doesn't run. Realtime-throttled inside
    // TryReconcile, so this is cheap. Root.Update is core RimWorld and always present.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class Patch_Root_Update_KmhMarkers
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (Find.World != null)
                Features.Sites.WorldComponent_KMHSiteMarkers.TryReconcile(0.5f);
        }
    }
}
