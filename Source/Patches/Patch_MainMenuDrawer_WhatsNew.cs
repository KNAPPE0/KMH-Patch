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
            KmhDuplicateModCheck.ShowPending();
            if (shown) return;
            shown = true;

            KMHPatchSettings s = KMHPatchMod.Settings;
            (bool welcomeWanted, bool whatsNewWanted) = Wanted(s?.ShowWelcomeOnLaunch ?? true, s?.WelcomeSeen ?? false,
                                                               s?.ShowWhatsNewOnUpdate ?? true, s?.WhatsNewSeen ?? "", KmhProtocol.DisplayVersion);
            // A new player gets the welcome; the changelog is for those who knew the previous version.
            if (s != null && welcomeWanted && s.WhatsNewSeen != KmhProtocol.DisplayVersion) { s.WhatsNewSeen = KmhProtocol.DisplayVersion; KMHPatchMod.SaveSettings(); }

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

        // Welcome once ever, What's New once per version and never on a first launch, or a new player gets four windows.
        internal static (bool welcome, bool whatsNew) Wanted(bool showWelcome, bool welcomeSeen, bool showWhatsNew, string whatsNewSeen, string build)
            => (showWelcome && !welcomeSeen,
                showWhatsNew && (welcomeSeen || !showWelcome) && !string.Equals(whatsNewSeen ?? "", build ?? "", System.StringComparison.Ordinal));
    }
}
