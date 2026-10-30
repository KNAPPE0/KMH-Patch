using KMHPatch.Diagnostics;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.PlayerStats
{
    internal static class PlayerStatsHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.PlayerStatsSnapshot, OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ColonistProfile,      OnColonistProfile);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ColonistRoster,       OnColonistRoster);
        }

        public static bool RequestColonistRoster() => KmhDispatcher.Send(KmhProtocol.Kind.ColonistRosterRequest, null);

        private static void OnColonistRoster(KmhEnvelope env)
        {
            ColonistRosterSnapshot snap = env?.DataAs<ColonistRosterSnapshot>();
            if (snap != null) ColonistRosterCache.Apply(snap);
        }

        public static bool RequestSnapshot()
        {
            return KmhDispatcher.Send(KmhProtocol.Kind.PlayerStatsRequest, null);
        }

        public static bool SendColonyReport(ColonyReport report)
        {
            return report != null && KmhDispatcher.Send(KmhProtocol.Kind.ColonyReport, report);
        }

        public static bool RequestColonist(string username)
        {
            return !string.IsNullOrEmpty(username)
                && KmhDispatcher.Send(KmhProtocol.Kind.ColonistRequest, new { username });
        }

        private static void OnColonistProfile(KmhEnvelope env)
        {
            ColonistProfileEnvelope payload = env?.DataAs<ColonistProfileEnvelope>();
            if (payload == null || string.IsNullOrEmpty(payload.Username)) return;
            // Written by another player's game and drawn as rich text, so made inert here.
            if (payload.Detail?.RecentCombat != null)
                payload.Detail.RecentCombat = payload.Detail.RecentCombat.ConvertAll(s => KMHPatch.UI.KmhDisplayText.Inert(s, 120));
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
