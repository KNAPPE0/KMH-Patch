using HarmonyLib;
using KMHPatch.Dialogs;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    // "KMH" overlay button top-right of the main menu (under the version badge), opens About overlaid instead of
    // inserted into DoMainMenuControls - RWT shares that list and order is finicky
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_AboutButton
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            const float width   = 90f;
            const float height  = 28f;
            const float padding = 8f;

            // Top-right, just below the version badge (badge is ~22px tall at y=8).
            Rect r = new Rect(
                Verse.UI.screenWidth - width - padding,
                padding + 26f,
                width,
                height);

            if (Widgets.ButtonText(r, "KMH"))
            {
                Find.WindowStack.Add(new Dialog_KMHAbout());
            }
        }
    }
}
