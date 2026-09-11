using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Dialogs;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // MainMenuOnGUI rather than UIRoot_Entry.Init, which can run before WindowStack is ready.
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_MainMenuOnGUI
    {
        private static bool shown = false;

        public static void ResetShownFlag() => shown = false;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (shown) return;
            shown = true;

            // Settings can still be null here if the Mod ctor has not run.
            if (KMHPatchMod.Settings != null && !KMHPatchMod.Settings.ShowWelcomeOnLaunch)
            {
                return;
            }

            // Let the what's-new changelog take the slot this launch so the two don't stack.
            if (Dialogs.KmhMainMenuPopups.WhatsNewPending) return;

            // Defer one frame so any menu-startup tasks finish first.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Find.WindowStack == null) return;
                Find.WindowStack.Add(new Dialog_KMHWelcome());
                KmhLog.Info("Welcome dialog opened");
            });
        }
    }
}
