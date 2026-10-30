using HarmonyLib;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // The single draw hook for KMH's menu surfaces: separate postfixes drew in Harmony's registration order.
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_KmhSurface
    {
        private static bool _faulted;

        [HarmonyPostfix]
        private static void Postfix()
        {
            // Logged once: this runs every frame, and the menu must render regardless.
            try { UI.KmhMainMenuSurface.Draw(); }
            catch (System.Exception ex)
            {
                if (_faulted) return;
                _faulted = true;
                Diagnostics.KmhLog.Warn($"KMH main menu draw failed, and is not retried this session: {ex}");
            }
        }
    }
}
