using System;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Discord
{
    // Steam's connect string is a claim like a Discord invite, so both its launch-argument and callback routes
    // go through the Discord invite path and every check guarding it.
    internal static class KmhSteamJoin
    {
        private static bool _startupHandled;

        // Steam appends "+kmh <id>"; quoting varies by shell and Steam version, so both shapes are read.
        internal static bool TryReadCommandLine(string[] args, out string serverId)
        {
            serverId = "";
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrEmpty(a)) continue;

                if (KmhSteamPresence.TryReadServerId(a, out serverId)) return true;
                if (a.Trim() == "+kmh" && i + 1 < args.Length
                    && KmhSteamPresence.TryReadServerId(KmhSteamPresence.Prefix + (args[i + 1] ?? "").Trim(), out serverId))
                    return true;
            }
            return false;
        }

        // Runs once the directory exists; a launch argument still goes through the same gate and confirmation.
        public static void HandleStartup()
        {
            if (_startupHandled) return;
            _startupHandled = true;
            try
            {
                if (!TryReadCommandLine(Environment.GetCommandLineArgs(), out string serverId)) return;
                KmhLog.Info("Steam: RimWorld was launched from a friend's Join Game.");
                Accept(serverId);
            }
            catch (Exception ex) { KmhLog.Debug($"Steam: reading the launch arguments failed ({ex.GetType().Name})."); }
        }

        // Routed through the Discord path so a Steam join gets every check by construction, not by copy.
        public static void Accept(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return;
            KmhPresenceJoin.Accept(KmhPresenceJoin.Prefix + serverId);
        }

        internal static void ResetForTest() => _startupHandled = false;
    }
}
