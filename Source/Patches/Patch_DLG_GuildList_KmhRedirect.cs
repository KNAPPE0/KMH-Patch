using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Guilds;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Patches
{
    // Redirect RWT's stock guild list (GameClient.Dialogs.DLG_GuildList) to KMH's Dialog_KMHGuildHall on a KMH server.
    // Stock RWT's list is a flat "Username - Rank" scroll; the Guild Hall adds Manage, MOTD, perks, diplomacy, settings.
    //
    // Manually patched (NOT an attribute patch) on purpose: the type was removed/renamed in newer RWT builds, and a
    // compile-time [HarmonyPatch(typeof(DLG_GuildList))] / typed parameter would trip ReflectionTypeLoadException when
    // this class's metadata is scanned on a build where the type is absent. We resolve it by name and skip cleanly.
    // Only redirects on a KMH server (the GuildCache is empty otherwise, so the Hall would sit on "Loading...").
    internal static class Patch_DLG_GuildList_KmhRedirect
    {
        // Called once from KmhEntry after the attribute patches apply. Safe on every RWT build.
        public static void TryApply(Harmony harmony)
        {
            try
            {
                Type t = AccessTools.TypeByName("GameClient.Dialogs.DLG_GuildList");
                if (t == null)
                {
                    KmhLog.Info("Guild redirect: DLG_GuildList not present in this RWT build - skipped (use the KMH tab's Guild Hall).");
                    return;
                }

                // Match the "open with a member list" constructor without naming RWT's member type (it may have been
                // renamed): the single-arg ctor whose parameter is a List<>. Patching only it avoids double-firing on
                // chained constructors.
                ConstructorInfo target = null;
                foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    ParameterInfo[] ps = c.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType.IsGenericType &&
                        ps[0].ParameterType.GetGenericTypeDefinition() == typeof(List<>))
                    { target = c; break; }
                }
                if (target == null)
                {
                    KmhLog.Info("Guild redirect: DLG_GuildList has no List<> constructor in this build - skipped.");
                    return;
                }

                HarmonyMethod post = new HarmonyMethod(typeof(Patch_DLG_GuildList_KmhRedirect)
                    .GetMethod(nameof(RedirectPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(target, postfix: post);
                KmhLog.Info("Guild redirect: armed on DLG_GuildList.");
            }
            catch (Exception ex) { KmhLog.Warn($"Guild redirect setup threw: {ex.Message}"); }
        }

        // __instance typed as object (no compile-time RWT ref); DLG_GuildList is a Verse.Window, so Close works via base.
        private static void RedirectPostfix(object __instance)
        {
            if (!KmhDispatcher.IsKmhServer) return;
            if (!(__instance is Window win)) return;

            // Closing a window inside its own ctor corrupts the WindowStack - swap a frame later.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    win.Close(doCloseSound: false);
                    Find.WindowStack.Add(new Dialog_KMHGuildHall());
                    KmhLog.Info("Redirected RWT DLG_GuildList → Dialog_KMHGuildHall");
                }
                catch (Exception ex) { KmhLog.Warn($"GuildList redirect threw: {ex.Message}"); }
            });
        }
    }
}
