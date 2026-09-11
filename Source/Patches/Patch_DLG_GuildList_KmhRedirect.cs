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
    // Patched manually, not by attribute: the type is absent in some RWT builds and a typed reference would throw during scanning.
    internal static class Patch_DLG_GuildList_KmhRedirect
    {
        public static void TryApply(Harmony harmony)
        {
            try
            {
                // RWT has both moved and renamed this type, so every known location is offered.
                string ns = RwtCompat.ClientNsRoot + ".Dialogs";
                Type t = RwtCompat.ResolveType(ns, "DLG_GuildList", ns + ".DLG_GuildList", ns + ".Guild.DLG_GuildList");
                if (t == null)
                {
                    KmhLog.Info("Guild redirect: DLG_GuildList not present in this RWT build - skipped (use the KMH tab's Guild Hall).");
                    return;
                }

                // Matched by shape, not by RWT's member type name, and only one ctor so chained ones cannot double-fire.
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
