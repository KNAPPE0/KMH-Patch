using KMHPatch.Diagnostics;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.PlayerStats
{
    // Registers player-stats handlers and the RequestSnapshot entry point during KMH bootstrap.
    internal static class PlayerStatsHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.PlayerStatsSnapshot, OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ColonistProfile,      OnColonistProfile);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ColonistRoster,       OnColonistRoster);
        }

        // Ask the server for the flattened colonist roster (every colony's colonists) for the Colonist Records board.
        public static bool RequestColonistRoster() => KmhDispatcher.Send(KmhProtocol.Kind.ColonistRosterRequest, null);

        private static void OnColonistRoster(KmhEnvelope env)
        {
            ColonistRosterSnapshot snap = env?.DataAs<ColonistRosterSnapshot>();
            if (snap != null) ColonistRosterCache.Apply(snap);
        }

        // Requests the latest leaderboard snapshot; safe before handshake since Send no-ops off KMH servers.
        public static bool RequestSnapshot()
        {
            return KmhDispatcher.Send(KmhProtocol.Kind.PlayerStatsRequest, null);
        }

        // Upload this colony's summary + colonist. Sent on a timer by GameComponent_KMHColonyReporter; no-ops off a KMH server.
        public static bool SendColonyReport(ColonyReport report)
        {
            return report != null && KmhDispatcher.Send(KmhProtocol.Kind.ColonyReport, report);
        }

        // Ask the server for a player's full colonist profile (Bio/Health/Combat). Fired when a player card opens.
        public static bool RequestColonist(string username)
        {
            return !string.IsNullOrEmpty(username)
                && KmhDispatcher.Send(KmhProtocol.Kind.ColonistRequest, new { username });
        }

        private static void OnColonistProfile(KmhEnvelope env)
        {
            ColonistProfileEnvelope payload = env?.DataAs<ColonistProfileEnvelope>();
            if (payload == null || string.IsNullOrEmpty(payload.Username)) return;
            ColonistProfileCache.Apply(payload.Username, payload.Detail);
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
