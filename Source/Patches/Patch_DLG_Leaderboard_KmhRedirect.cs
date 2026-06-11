using System;
using GameClient.Dialogs;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.PlayerStats;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Patches
{
    // Redirect stock DLG_Leaderboard to Dialog_KMHPlayerLeaderboard on KMH servers. Gated on IsKmhServer so stock
    // servers keep the stock dialog. Constructor Postfix schedules Close+Add via Long EventHandler so the redirect
    // fires after the original add
    [HarmonyPatch(typeof(DLG_Leaderboard))]
    [HarmonyPatch(MethodType.Constructor, new Type[] { typeof(FL_Leaderboard) })]
    internal static class Patch_DLG_Leaderboard_KmhRedirect
    {
        [HarmonyPostfix]
        private static void Postfix(DLG_Leaderboard __instance)
        {
            if (!KmhDispatcher.IsKmhServer) return;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    __instance.Close(doCloseSound: false);
                    Find.WindowStack.Add(new Dialog_KMHPlayerLeaderboard());
                    KmhLog.Info("Redirected RWT DLG_Leaderboard → Dialog_KMHPlayerLeaderboard");
                }
                catch (Exception ex)
                {
                    KmhLog.Warn($"Leaderboard redirect threw: {ex.Message}");
                }
            });
        }
    }
}
