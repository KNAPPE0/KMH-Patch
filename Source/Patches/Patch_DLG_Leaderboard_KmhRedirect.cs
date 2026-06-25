using System;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Standings;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Patches
{
    // Redirect RWT's stock DLG_Leaderboard to the KMH Server Standings hub on a KMH server. Gated on IsKmhServer so
    // stock servers keep the stock dialog.
    //
    // Manually patched (NOT an attribute patch) so a removed/renamed DLG_Leaderboard or FL_Leaderboard in newer RWT
    // builds can't trip ReflectionTypeLoadException during type scanning - we resolve the type by name and skip if
    // absent. (RWT's "ram improvements" rework touched the leaderboard packet path, so this type is a real risk.)
    internal static class Patch_DLG_Leaderboard_KmhRedirect
    {
        public static void TryApply(Harmony harmony)
        {
            try
            {
                Type t = AccessTools.TypeByName("GameClient.Dialogs.DLG_Leaderboard");
                if (t == null)
                {
                    KmhLog.Info("Standings redirect: DLG_Leaderboard not present in this RWT build - skipped (use the KMH tab's Server Standings).");
                    return;
                }

                // The "open with leaderboard data" constructor - the single-argument ctor - without naming RWT's data
                // type (it may have been renamed by the leaderboard rework).
                ConstructorInfo target = null;
                foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    if (c.GetParameters().Length == 1) { target = c; break; }
                if (target == null)
                {
                    KmhLog.Info("Standings redirect: DLG_Leaderboard has no single-arg constructor in this build - skipped.");
                    return;
                }

                HarmonyMethod post = new HarmonyMethod(typeof(Patch_DLG_Leaderboard_KmhRedirect)
                    .GetMethod(nameof(RedirectPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(target, postfix: post);
                KmhLog.Info("Standings redirect: armed on DLG_Leaderboard.");
            }
            catch (Exception ex) { KmhLog.Warn($"Standings redirect setup threw: {ex.Message}"); }
        }

        private static void RedirectPostfix(object __instance)
        {
            if (!KmhDispatcher.IsKmhServer) return;
            if (!(__instance is Window win)) return;

            // Close+swap a frame later - closing a window inside its own ctor corrupts the WindowStack.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    win.Close(doCloseSound: false);
                    Find.WindowStack.Add(new Dialog_KMHStandings());
                    KmhLog.Info("Redirected RWT DLG_Leaderboard → Server Standings");
                }
                catch (Exception ex) { KmhLog.Warn($"Leaderboard redirect threw: {ex.Message}"); }
            });
        }
    }
}
