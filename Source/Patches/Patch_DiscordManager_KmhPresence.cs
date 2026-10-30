using System;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;

namespace KMHPatch.Patches
{
    // Two apps setting a presence show two activities, so RWT's is suppressed only while KMH drives one.
    internal static class Patch_DiscordManager_KmhPresence
    {
        internal static int Suppressed;

        // Plain reflection, not AccessTools: the self-test runs this outside the game, where Harmony is not loaded.
        internal static Type ManagerType()
        {
            try
            {
                foreach (string name in new[] { "RTClient.Managers.DiscordManager",
                                                "GameClient.Managers.DiscordManager", "DiscordManager" })
                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type t = a.GetType(name, throwOnError: false);
                        if (t != null) return t;
                    }
                return null;
            }
            catch { return null; }
        }

        // StartPresence builds RWT's client and sets its presence in one call; no generation has a SetPresence to hook.
        // Not named TargetMethod/Prefix: Harmony applies those names on any class it is handed, patching this twice.
        internal static MethodBase StartPresenceMethod()
        {
            Type t = ManagerType();
            try
            {
                return t?.GetMethod("StartPresence",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            }
            catch { return null; }
        }

        internal static bool SuppressRwtPresence()
        {
            if (!Features.Discord.KmhRichPresence.Active) return true;
            Suppressed++;
            return false;
        }

        // By hand, not by attribute: RWT's assembly name changes between generations and a missing attribute target throws.
        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodBase target = StartPresenceMethod();
                if (target == null)
                {
                    KmhLog.Debug("Discord: RWT exposes no DiscordManager.StartPresence here - nothing to suppress.");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(Patch_DiscordManager_KmhPresence).GetMethod(nameof(SuppressRwtPresence),
                        BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception ex) { KmhLog.Debug($"Discord: could not patch RWT's presence ({ex.GetType().Name})."); }
        }
    }
}
