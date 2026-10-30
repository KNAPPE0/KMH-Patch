using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Reputation.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Reputation
{
    // Pushed on handshake and after quest activity, so nothing here polls.
    public static class ReputationCache
    {
        private static Dictionary<string, ReputationEntryDto> _byUser
            = new Dictionary<string, ReputationEntryDto>(StringComparer.OrdinalIgnoreCase);

        public static event Action Updated;

        public static bool HasSnapshot { get; private set; }

        // Bumped on every snapshot, so views can cache a sorted copy instead of rebuilding one per frame.
        public static int Version { get; private set; }

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ReputationSnapshot, OnSnapshot);
        }

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.ReputationRequest, null);

        public static void Clear() { _byUser = new Dictionary<string, ReputationEntryDto>(StringComparer.OrdinalIgnoreCase); HasSnapshot = false; Version++; }

        public static List<ReputationEntryDto> Leaderboard()
        {
            List<ReputationEntryDto> list = new List<ReputationEntryDto>(_byUser.Values);
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            return list;
        }

        public static string TierFor(string username)
        {
            if (string.IsNullOrEmpty(username)) return "";
            return _byUser.TryGetValue(username, out ReputationEntryDto e) ? e.Tier : "";
        }

        // Only the trusted and the unreliable are called out; neutral players get nothing, so boards stay uncluttered.
        public static string Badge(string username)
        {
            switch (TierFor(username))
            {
                case "Trusted":    return " <color=#4cc38a>[Trusted]</color>";
                case "Unreliable": return " <color=#f07167>[Unreliable]</color>";
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
            Version++;
            KmhCacheEvents.Raise(Updated, "Reputation");
        }
    }
}
