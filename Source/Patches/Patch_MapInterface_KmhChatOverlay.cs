using System;
using HarmonyLib;
using RimWorld;

namespace KMHPatch.Patches
{
    // After the main tabs and before the window stack: windows cover the overlay, and it never takes a click.
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_AfterMainTabs))]
    internal static class Patch_MapInterface_KmhChatOverlay
    {
        private static bool _reported;

        [HarmonyPostfix]
        private static void Postfix()
        {
            try { Features.Chat.ChatOverlay.Draw(); }
            catch (Exception ex)
            {
                if (_reported) return;
                _reported = true;
                Diagnostics.KmhLog.Warn($"Colony Nexus chat overlay could not draw and is reported once: {ex.Message}");
            }
        }
    }
}
