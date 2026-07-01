using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Dialogs;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // Show the KMH welcome dialog once per RimWorld launch, the first time the main menu finishes drawing
    //
    // Why MainMenuOnGUI and not UIRoot_Entry.Init: Init runs before WindowStack is fully ready in some cases;
    // MainMenuOnGUI is guaranteed to be in the GUI loop, so Find.WindowStack.Add will always work
    //
    // The static `shown` flag is process-scoped - a fresh launch shows it again. (When we want "show only once
    // ever", swap to a flag file in the mod config dir.)
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_MainMenuOnGUI
    {
        private static bool shown = false;

        // Lets the settings panel ('Show welcome dialog again on next return to main menu' button) clear the
        // once-per-process flag so the welcome pops back up without needing a full game restart
        public static void ResetShownFlag() => shown = false;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (shown) return;
            shown = true;

            // Player can disable the auto-show in Mods->Settings->KMH Patch. Settings may be null on the very first
            // tick if the Mod ctor hasn't run yet (defensive - shouldn't happen in practice)
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
