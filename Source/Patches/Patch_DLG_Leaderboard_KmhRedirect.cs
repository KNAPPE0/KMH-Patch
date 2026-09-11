using System;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Standings;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Patches
{
    // Patched manually, not by attribute: a removed or renamed leaderboard then skips cleanly instead of throwing during scanning.
    internal static class Patch_DLG_Leaderboard_KmhRedirect
    {
        public static void TryApply(Harmony harmony)
        {
            try
            {
                string ns = RwtCompat.ClientNsRoot + ".Dialogs";
                Type t = RwtCompat.ResolveType(ns, "DLG_Leaderboard", ns + ".DLG_Leaderboard");
                if (t == null)
                {
                    KmhLog.Info("Standings redirect: DLG_Leaderboard not present in this RWT build - skipped (use the KMH tab's Server Standings).");
                    return;
                }

                // Matched by arity, not by RWT's data type name, which the leaderboard rework may have changed.
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
