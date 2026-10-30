using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Enforcement.Patches
{
    // Virtual targets: every override needs its own Harmony patch, hence install-on-demand.
    internal static class EnforcementSettingsPatches
    {
        private static bool _installed;

        public static void EnsureInstalled()
        {
            if (_installed) return;
            _installed = true;
            ModSettingsWriteGuard.EnsureInstalled();
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Harmony h = KmhEntry.HarmonyInstance ?? new Harmony(Constants.HarmonyId);
                var drawPrefix   = new HarmonyMethod(typeof(EnforcementSettingsPatches), nameof(DrawPrefix));
                var drawPostfix  = new HarmonyMethod(typeof(EnforcementSettingsPatches), nameof(DrawPostfix));
                var writePrefix  = new HarmonyMethod(typeof(EnforcementSettingsPatches), nameof(WritePrefix));

                int n = 0;
                foreach (MethodBase m in Targets(nameof(Verse.Mod.DoSettingsWindowContents), new[] { typeof(Rect) }))
                { try { h.Patch(m, prefix: drawPrefix, postfix: drawPostfix); n++; } catch { } }
                foreach (MethodBase m in Targets(nameof(Verse.Mod.WriteSettings), Type.EmptyTypes))
                { try { h.Patch(m, prefix: writePrefix); n++; } catch { } }
                KmhLog.Info($"Enforcement: mod-settings lock installed ({n} method(s), {sw.ElapsedMilliseconds}ms)");
            }
            catch (Exception ex) { KmhLog.Error($"Enforcement: settings-lock install failed: {ex}"); }
        }

        private static IEnumerable<MethodBase> Targets(string method, Type[] args)
        {
            yield return AccessTools.Method(typeof(Verse.Mod), method);
            foreach (Type t in typeof(Verse.Mod).AllSubclassesNonAbstract())
            {
                MethodInfo m = AccessTools.DeclaredMethod(t, method, args);
                if (m != null) yield return m;
            }
        }

        private static bool _diagLogged;

        // Positional __args[0]: overrides name the Rect parameter differently, which breaks a named binding.
        public static bool DrawPrefix(Verse.Mod __instance, object[] __args)
        {
            if (!_diagLogged)
            {
                _diagLogged = true;
                KmhLog.Debug($"Enforcement: mod-settings patch firing for '{__instance?.Content?.Name}' - {EnforcementCache.DiagState(__instance?.Content)}");
            }

            if (EnforcementCache.IsModEditable(__instance)) return true;
            Rect inRect = (__args != null && __args.Length > 0 && __args[0] is Rect r) ? r : default;
            EnforcementLockUI.DrawLockedNotice(inRect, __instance);
            return false;
        }

        public static void DrawPostfix(Verse.Mod __instance, object[] __args)
        {
            if (__args != null && __args.Length > 0 && __args[0] is Rect r)
                EnforcementAdminButton.Draw(r, __instance);
        }

        public static bool WritePrefix(Verse.Mod __instance)
            => EnforcementCache.IsModEditable(__instance);

        // Safety net: the first Options open installs the lock, so startup never pays for it.
        [HarmonyPatch(typeof(RimWorld.Dialog_Options), MethodType.Constructor, new Type[0])]
        internal static class Patch_Dialog_Options_Ctor
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (EnforcementCache.IsLockActive() || EnforcementProfileApplier.RestoreAwaitsRestart
                    || (SubProtocol.KmhDispatcher.IsKmhServer && EnforcementCache.IsAdmin))
                    EnsureInstalled();
            }
        }
    }
}
