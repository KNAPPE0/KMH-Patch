using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Dialogs;
using KMHPatch.SubProtocol;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // Shows the what's-new changelog once per launch (it takes the single popup slot; the welcome patch yields to it).
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_WhatsNew
    {
        private static bool shown;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (shown) return;
            shown = true;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Find.WindowStack == null) return;
                Find.WindowStack.Add(new Dialog_KMHWhatsNew(chainWelcome: true));
                KmhLog.Info($"What's-new dialog opened for build {KmhProtocol.BuildVersion}");
            });
        }
    }
}
