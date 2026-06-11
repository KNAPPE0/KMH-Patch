using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Enforcement.Patches
{
    // Locks a non-safe mod's options: draws a "locked" notice instead of its settings and skips WriteSettings. Safe
    // mods + exempt admins pass through. DoSettingsWindowContents/WriteSettings are virtual, so the base AND every
    // override get patched - which is expensive (one Harmony patch per mod), so it is NOT part of startup PatchAll.
    // EnsureInstalled() runs it on demand: enforcement active, admin on a KMH server, or first Options open
    internal static class EnforcementSettingsPatches
    {
        private static bool _installed;

        public static void EnsureInstalled()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Harmony h = KmhEntry.HarmonyInstance ?? new Harmony(Constants.HarmonyId);
                var drawPrefix   = new HarmonyMethod(typeof(EnforcementSettingsPatches), nameof(DrawPrefix));
                var drawPostfix  = new HarmonyMethod(typeof(EnforcementSettingsPatches), nameof(DrawPostfix));
                var writePrefix  = new HarmonyMethod(typeof(EnforcementSettingsPatches), nameof(WritePrefix));

                int n = 0;
                foreach (MethodBase m in DrawTargets())  { try { h.Patch(m, prefix: drawPrefix, postfix: drawPostfix); n++; } catch { } }
                foreach (MethodBase m in WriteTargets()) { try { h.Patch(m, prefix: writePrefix); n++; } catch { } }
                KmhLog.Info($"Enforcement: mod-settings lock installed ({n} method(s), {sw.ElapsedMilliseconds}ms)");
            }
            catch (Exception ex) { KmhLog.Error($"Enforcement: settings-lock install failed: {ex}"); }
        }

        private static IEnumerable<MethodBase> DrawTargets()
        {
            yield return AccessTools.Method(typeof(Verse.Mod), nameof(Verse.Mod.DoSettingsWindowContents));
            foreach (Type t in typeof(Verse.Mod).AllSubclassesNonAbstract())
            {
                MethodInfo m = AccessTools.DeclaredMethod(t, nameof(Verse.Mod.DoSettingsWindowContents), new[] { typeof(Rect) });
                if (m != null) yield return m;
            }
        }

        private static IEnumerable<MethodBase> WriteTargets()
        {
            yield return AccessTools.Method(typeof(Verse.Mod), nameof(Verse.Mod.WriteSettings));
            foreach (Type t in typeof(Verse.Mod).AllSubclassesNonAbstract())
            {
                MethodInfo m = AccessTools.DeclaredMethod(t, nameof(Verse.Mod.WriteSettings), Type.EmptyTypes);
                if (m != null) yield return m;
            }
        }

        private static bool _diagLogged;

        // __args[0] is the Rect; reading by position dodges per-override param-name differences that would break a
        // named Rect parameter
        public static bool DrawPrefix(Verse.Mod __instance, object[] __args)
        {
            if (!_diagLogged)
            {
                _diagLogged = true;
                KmhLog.Info($"Enforcement: mod-settings patch firing for '{__instance?.Content?.Name}' - {EnforcementCache.DiagState(__instance?.Content)}");
            }

            if (EnforcementCache.IsModEditable(__instance)) return true;
            Rect inRect = (__args != null && __args.Length > 0 && __args[0] is Rect r) ? r : default;
            EnforcementLockUI.DrawLockedNotice(inRect, __instance);
            return false;
        }

        // After a mod draws its settings (admins + safe mods reach this), an admin gets the one-click "mark safe /
        // unmark safe" button
        public static void DrawPostfix(Verse.Mod __instance, object[] __args)
        {
            if (__args != null && __args.Length > 0 && __args[0] is Rect r)
                EnforcementAdminButton.Draw(r, __instance);
        }

        public static bool WritePrefix(Verse.Mod __instance)
            => EnforcementCache.IsModEditable(__instance); // false = skip the write

        // Safety net: first Options open installs the lock if anything needs it (covers stale edge cases without
        // paying the cost at startup)
        [HarmonyPatch(typeof(RimWorld.Dialog_Options), MethodType.Constructor, new Type[0])]
        internal static class Patch_Dialog_Options_Ctor
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (EnforcementCache.IsLockActive()
                    || (SubProtocol.KmhDispatcher.IsKmhServer && EnforcementCache.IsAdmin))
                    EnsureInstalled();
            }
        }
    }
}
