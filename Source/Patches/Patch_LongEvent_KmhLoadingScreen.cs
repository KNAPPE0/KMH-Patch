using System;
using HarmonyLib;
using KMHPatch.UI;
using Verse;

namespace KMHPatch.Patches
{
    // Skips vanilla entirely, or its background, tip and mod list draw over KMH's; any other load or a failure stays vanilla.
    [HarmonyPatch(typeof(LongEventHandler), "LongEventsOnGUI")]
    internal static class Patch_LongEvent_FullScreen
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            try
            {
                if (!KmhLoadingScreen.FullScreenNow() || !KmhLoadingScreen.Wanted()) return true;
                KmhLoadingScreen.Draw();
                return false;
            }
            catch (Exception ex) { KmhLoadingScreen.Broken(ex); return true; }
        }
    }
}
