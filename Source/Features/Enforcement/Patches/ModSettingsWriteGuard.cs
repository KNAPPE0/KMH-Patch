using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Features.Enforcement.Patches
{
    // Every mod-settings save, Mod.WriteSettings or a mod's own ModSettings.Write, ends in WriteModSettings.
    internal static class ModSettingsWriteGuard
    {
        private static bool _installed;
        private static MethodInfo _fileName;
        private static readonly HashSet<string> _logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void EnsureInstalled()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                MethodInfo target = AccessTools.Method(typeof(LoadedModManager), "WriteModSettings");
                _fileName = AccessTools.Method(typeof(LoadedModManager), "GetSettingsFilename", new[] { typeof(string), typeof(string) });
                if (target == null) { KmhLog.Warn("Enforcement: RimWorld's mod-settings save was not found; the settings lock is the only guard."); return; }
                Harmony h = KmhEntry.HarmonyInstance ?? new Harmony(Constants.HarmonyId);
                h.Patch(target, prefix: new HarmonyMethod(typeof(ModSettingsWriteGuard), nameof(BeforeWrite)));
            }
            catch (Exception ex) { KmhLog.Warn($"Enforcement: the mod-settings save guard could not be installed: {ex.Message}"); }
        }

        // Positional, since the parameter names are RimWorld's to change.
        public static bool BeforeWrite(object[] __args)
        {
            if (__args == null || __args.Length < 2) return true;
            string file = FileName(__args[0] as string, __args[1] as string);
            bool restored = EnforcementProfileApplier.AwaitsRestart(file);
            // A mod saving its own settings directly would otherwise rewrite an enforced file and force a repair restart.
            bool locked = !restored && EnforcementProfileApplier.IsFileEnforced(file)
                && !EnforcementCache.IsModEditable(ModByFolder(__args[0] as string));
            if (!restored && !locked) return true;
            lock (_logged)
                if (_logged.Add(file))
                    KmhLog.Info(restored
                        ? $"Enforcement: kept RimWorld from saving '{file}' over the settings put back for you; they load after a restart."
                        : $"Enforcement: kept a mod from saving '{file}', which this server's settings lock.");
            return false;
        }

        private static ModContentPack ModByFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                if (string.Equals(m?.FolderName, folder, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        internal static string FileName(string modIdentifier, string handle)
        {
            if (string.IsNullOrEmpty(modIdentifier) || string.IsNullOrEmpty(handle)) return "";
            try
            {
                if (_fileName?.Invoke(null, new object[] { modIdentifier, handle }) is string full) return Path.GetFileName(full);
            }
            catch (Exception ex) { KmhLog.Debug($"Enforcement: settings file name fell back: {ex.Message}"); }
            return "Mod_" + modIdentifier + "_" + handle + ".xml";
        }
    }
}
