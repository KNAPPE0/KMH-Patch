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
