using System;
using HarmonyLib;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Patches
{
    // The one callback that still runs on the landing-site page and the paused world, where WorldComponentTick does not.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class Patch_Root_Update_KmhMarkers
    {
        private static readonly string[] Names = { "join hydration", "site markers", "roads" };

        // Both reconciles check for a world themselves, so there is nothing to gate here.
        private static readonly Action[] Steps =
        {
            SubProtocol.KmhHandshakeHandler.RetryPendingHydration,
            () => Features.Sites.WorldComponent_KMHSiteMarkers.TryReconcile(0.5f),
            () => Features.Roadworks.WorldComponent_KMHRoads.TryReconcile(0.5f),
        };

        [HarmonyPostfix]
        private static void Postfix() => KmhFrameSteps.Run(Names, Steps);
    }
}
