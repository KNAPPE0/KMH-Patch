using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Dialogs;
using KMHPatch.SubProtocol;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // The welcome popup chains after this one so the two never stack.
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_WhatsNew
    {
        private static bool shown;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (shown) return;
            shown = true;

            KMHPatchSettings s = KMHPatchMod.Settings;
            bool welcomeWanted  = s == null || s.ShowWelcomeOnLaunch;
            bool whatsNewWanted = s == null || s.ShowWhatsNewOnUpdate;   // every launch until disabled

            if (!whatsNewWanted && !welcomeWanted) return;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Find.WindowStack == null) return;
                if (whatsNewWanted)
                {
                    Find.WindowStack.Add(new Dialog_KMHWhatsNew(chainWelcome: welcomeWanted));
                    KmhLog.Info($"What's-new dialog opened for build {KmhProtocol.DisplayVersion}");
                }
                else if (welcomeWanted)
                {
                    Find.WindowStack.Add(new Dialog_KMHWelcome());
                }
            });
        }
    }
}
