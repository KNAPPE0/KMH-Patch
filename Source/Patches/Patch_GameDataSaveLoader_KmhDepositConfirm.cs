using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Treasury;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // The fast path only: ExposeData and a self-heal loop still cover save paths that bypass this one.
    [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame))]
    internal static class Patch_GameDataSaveLoader_KmhDepositConfirm
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { GameComponent_KMHDepositLedger.Instance?.NotifySaved("GameDataSaveLoader.SaveGame"); }
            catch (System.Exception ex) { KmhLog.Warn($"Deposit post-save confirm threw: {ex.Message}"); }
        }
    }

    // Permadeath saves outside RWT's force-save used the colony name, so RWT's upload read a missing or stale file.
    [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame))]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_GameDataSaveLoader_RwtSaveName
    {
        [HarmonyPrefix]
        private static void Prefix(ref string fileName)
        {
            try
            {
                string chosen = KmhSaveNow.SaveNameFor(fileName, KmhSaveNow.OnlineSaveName());
                if (chosen == fileName) return;
                KmhLog.Info($"Save: writing '{chosen}', the file RimWorld Together uploads, instead of '{fileName}'.");
                fileName = chosen;
                if (Find.GameInfo != null && Find.GameInfo.permadeathMode) Find.GameInfo.permadeathModeUniqueName = chosen;
            }
            catch (System.Exception ex) { KmhLog.Warn($"Save name check threw: {ex.Message}"); }
        }

        internal static string PrefixForTest(string fileName) { Prefix(ref fileName); return fileName; }
    }

    // Vanilla + modded autosave route through Autosaver; modded interval changes only affect timing, not this path.
    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.DoAutosave))]
    internal static class Patch_Autosaver_KmhDepositConfirm
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { GameComponent_KMHDepositLedger.Instance?.NotifySaved("autosave"); }
            catch (System.Exception ex) { KmhLog.Warn($"Deposit autosave confirm threw: {ex.Message}"); }
        }
    }
}
