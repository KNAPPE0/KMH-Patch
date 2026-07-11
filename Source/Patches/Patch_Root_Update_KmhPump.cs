using HarmonyLib;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Patches
{
    // Drains KmhMainThread each frame on the main thread; Root.Update is core RimWorld, always present
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class Patch_Root_Update_KmhPump
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            KmhMainThread.Pump();
            KmhDebugUplink.Pump();
        }
    }
}
