using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    // Where the game puts its menu this frame, so the Colony Nexus card can stand beside it on any version or screen.
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
    internal static class Patch_MainMenuDrawer_PaneRect
    {
        [HarmonyPrefix]
        private static void Prefix(Rect rect)
        {
            // The in-game menu draws through the same method; only the main menu's placement is wanted.
            if (Current.ProgramState == ProgramState.Entry) UI.KmhMainMenuSurface.VanillaPane = rect;
        }
    }
}
