using System;
using KMHPatch.Diagnostics;
using Steamworks;

namespace KMHPatch.Features.Discord
{
    // The only compile-time Steamworks reference: Callback<T> cannot be registered by reflection. Callers wrap it,
    // so an install without Steamworks loses Join Game rather than KMH.
    internal static class KmhSteamJoinCallback
    {
        // Held as object so this class loads without Steamworks; only method bodies name the type.
        private static object _handle;

        public static bool Registered => _handle != null;

        public static void Register()
        {
            if (_handle != null) return;
            if (!Account.KmhSteamPersona.SteamIsRunning) return;
            try
            {
                _handle = Callback<GameRichPresenceJoinRequested_t>.Create(OnJoinRequested);
                KmhLog.Debug("Steam: listening for Join Game while the game is running.");
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"Steam: Join Game callbacks are unavailable here ({ex.GetType().Name}).");
                _handle = null;
            }
        }

        private static void OnJoinRequested(GameRichPresenceJoinRequested_t e)
        {
            try
            {
                // From another player's Steam client, so parsed strictly: anything KMH did not issue is refused.
                if (!KmhSteamPresence.TryReadServerId(e.m_rgchConnect ?? "", out string serverId))
                {
                    KmhLog.Warn("Steam: a Join Game request arrived in a shape KMH does not issue - ignored.");
                    return;
                }
                KmhLog.Info("Steam: a friend's Join Game was accepted.");
                KmhSteamJoin.Accept(serverId);
            }
            catch (Exception ex) { KmhLog.Warn($"Steam: handling Join Game failed ({ex.GetType().Name})."); }
        }
    }
}
