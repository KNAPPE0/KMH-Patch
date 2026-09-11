using System;
using System.Collections.Generic;
using KMHPatch.SubProtocol;
using UnityEngine;

namespace KMHPatch.Features.Chat
{
    // Who is on this server, from the server's own connection list - a name here is never a claim a client made.
    internal static class ChatRosterCache
    {
        private const float RefreshSeconds = 20f;

        private static readonly List<string> _online = new List<string>();
        private static readonly List<string> _offline = new List<string>();
        private static readonly Dictionary<string, long> _seen = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> _active = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static float _askedAt = -999f;

        public static IReadOnlyList<string> Online  => _online;
        public static IReadOnlyList<string> Offline => _offline;
        public static bool Known => _online.Count > 0 || _offline.Count > 0;

        // Bumped on every roster the server sends, so views can cache rows instead of rebuilding them per frame.
        public static int Version { get; private set; }

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ChatRoster, OnRoster);
        }

        // Called while the window is open; the interval is here so every caller cannot turn it into a poll storm.
        public static void RequestIfStale()
        {
            if (Time.realtimeSinceStartup - _askedAt < RefreshSeconds) return;
            _askedAt = Time.realtimeSinceStartup;
            KmhDispatcher.Send(KmhProtocol.Kind.ChatRosterRequest, null);
        }

        public static bool IsOnline(string name)
        {
            foreach (string n in _online) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsPlayer(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (IsOnline(name)) return true;
            foreach (string n in _offline) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Names that start with what is being typed, online first: the person you are talking to is usually here.
        public static List<string> Match(string prefix, int max)
        {
            var hits = new List<string>();
            if (prefix == null || max <= 0) return hits;

            // Blocked players are skipped: their messages are hidden, so offering to ping one is a way back in.
            foreach (string n in _online)
            {
                if (Suggestable(n, prefix)) hits.Add(n);
                if (hits.Count >= max) return hits;
            }
            foreach (string n in _offline)
            {
                if (Suggestable(n, prefix)) hits.Add(n);
                if (hits.Count >= max) return hits;
            }
            return hits;
        }

        private static bool Suggestable(string name, string prefix)
            => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && !(ChatModerationCache.BlockingEnabled && ChatModerationCache.IsBlocked(name));

        public static void Clear()
        {
            _online.Clear();
            _offline.Clear();
            _seen.Clear();
            _active.Clear();
            _askedAt = -999f;
            Version++;
        }

        // How long ago this player was last here, or "" for someone the server has never timed.
        public static string LastSeenOf(string name)
            => _seen.TryGetValue(name ?? "", out long ticks) ? KmhAgo.Since(ticks) : "";

        // Time spent actually playing here, or "" when the server has none recorded.
        public static string ActiveOf(string name)
            => _active.TryGetValue(name ?? "", out long seconds) && seconds > 0 ? KmhAgo.Span(seconds) : "";

        private static void OnRoster(KmhEnvelope env)
        {
            _online.Clear();
            _offline.Clear();
            _seen.Clear();
            _active.Clear();
            foreach (string n in env.GetStringArray("online")) if (!string.IsNullOrWhiteSpace(n)) _online.Add(n);
            foreach (string n in env.GetStringArray("offline")) if (!string.IsNullOrWhiteSpace(n)) _offline.Add(n);

            // Positional, so a length mismatch is dropped whole - one player's time under another's name is worse than none.
            Join(_seen, _offline, env.GetLongArray("seen"));
            Join(_active, _online, env.GetLongArray("active"));
            Version++;
        }

        internal static bool Join(Dictionary<string, long> into, List<string> names, long[] values)
        {
            if (values == null || names == null || values.Length != names.Count) return false;
            for (int i = 0; i < names.Count; i++) into[names[i]] = values[i];
            return true;
        }
    }
}
