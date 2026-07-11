using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Reputation.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Reputation
{
    // Holds the latest reputation roster and registers its snapshot handler. The quest board reads tiers from here
    // to badge poster/claimer names; the server pushes it on handshake and after quest activity, so no polling
    public static class ReputationCache
    {
        private static Dictionary<string, ReputationEntryDto> _byUser
            = new Dictionary<string, ReputationEntryDto>(StringComparer.OrdinalIgnoreCase);

        public static event Action Updated;

        public static bool HasSnapshot { get; private set; }

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ReputationSnapshot, OnSnapshot);
        }

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.ReputationRequest, null);

        public static void Clear() { _byUser = new Dictionary<string, ReputationEntryDto>(StringComparer.OrdinalIgnoreCase); HasSnapshot = false; }

        // Full roster sorted by score desc - drives the reputation board.
        public static List<ReputationEntryDto> Leaderboard()
        {
            List<ReputationEntryDto> list = new List<ReputationEntryDto>(_byUser.Values);
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            return list;
        }

        // Tier for a username, or "" if we have no record (unknown / neutral players simply get no badge)
        public static string TierFor(string username)
        {
            if (string.IsNullOrEmpty(username)) return "";
            return _byUser.TryGetValue(username, out ReputationEntryDto e) ? e.Tier : "";
        }

        // Coloured trust badge to append after a username. Only the trusted and the unreliable are called out;
        // neutral / unknown players get nothing, so boards stay uncluttered.
        public static string Badge(string username)
        {
            switch (TierFor(username))
            {
                case "Trusted":    return " <color=#80ff80>[Trusted]</color>";
                case "Unreliable": return " <color=#ff8080>[Unreliable]</color>";
                default:           return "";
            }
        }

        public static int ScoreFor(string username)
        {
            if (string.IsNullOrEmpty(username)) return 0;
            return _byUser.TryGetValue(username, out ReputationEntryDto e) ? e.Score : 0;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            ReputationSnapshot snap = env?.DataAs<ReputationSnapshot>();
            if (snap?.Entries == null) return;
            Dictionary<string, ReputationEntryDto> next = new Dictionary<string, ReputationEntryDto>(StringComparer.OrdinalIgnoreCase);
            foreach (ReputationEntryDto e in snap.Entries)
                if (!string.IsNullOrEmpty(e?.Username)) next[e.Username] = e;
            _byUser = next;
            HasSnapshot = true;
            try { Updated?.Invoke(); } catch (Exception ex) { KmhLog.Warn($"Reputation cache subscriber threw: {ex.Message}"); }
        }
    }
}
