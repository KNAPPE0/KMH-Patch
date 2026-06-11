using System;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Keeps a SAFE mod's config out of enforcement. RimWorld names settings files "Mod_<FolderName>_<Handle>.xml"
    // (FolderName = Workshop id for Steam mods), so we match the "Mod_<FolderName>_" prefix against each safe mod
    internal static class EnforcementMods
    {
        public static bool IsSafeModConfigFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            try
            {
                foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                {
                    if (m == null || string.IsNullOrEmpty(m.FolderName)) continue;
                    bool safe = EnforcementCache.IsSafeId(m.PackageId)
                             || EnforcementCache.IsSafeId(m.Name)
                             || EnforcementCache.IsSafeId(m.FolderName);
                    if (!safe) continue;

                    if (fileName.StartsWith("Mod_" + m.FolderName + "_", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception ex) { KmhLog.Warn($"IsSafeModConfigFile failed: {ex.Message}"); }
            return false;
        }
    }
}
