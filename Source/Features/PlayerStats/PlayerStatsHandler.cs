using KMHPatch.Diagnostics;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.PlayerStats
{
    // Registers the player-stats sub-protocol handlers with KmhDispatcher and exposes the public RequestSnapshot()
    // entry point that dialogs and auto-refresh timers call
    //
    // Wired from KMHPatchMod's bootstrap so handlers exist before any server message can possibly arrive
    internal static class PlayerStatsHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.PlayerStatsSnapshot, OnSnapshot);
        }

        // Ask the server for the current leaderboard. Idempotent - server
        // returns whatever the latest snapshot is, no rate-limited side
        // effect. KmhDispatcher.Send silently no-ops when not connected to a KMH server, so it's safe to call from
        // auto-refresh tickers before the handshake completes
        public static bool RequestSnapshot()
        {
            return KmhDispatcher.Send(KmhProtocol.Kind.PlayerStatsRequest, null);
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            PlayerStatsSnapshot snapshot = env?.DataAs<PlayerStatsSnapshot>();
            if (snapshot == null)
            {
                KmhLog.Warn("PlayerStats snapshot envelope had no parseable payload, ignoring");
                return;
            }
            PlayerStatsCache.Apply(snapshot);
        }
    }
}
