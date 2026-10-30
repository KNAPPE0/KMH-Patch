using System;
using System.Collections.Generic;

namespace KMHPatch.Features.Chat
{
    // Display only: the server enforces the list, so a blocked sender never reaches this client at all.
    public static class ChatModerationCache
    {
        private static readonly HashSet<string> _blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool BlockingEnabled { get; private set; } = true;

        public static event Action Updated;

        public static bool IsBlocked(string username)
        {
            if (string.IsNullOrEmpty(username)) return false;
            lock (_blocked) return _blocked.Contains(username);
        }

        public static List<string> Blocked()
        {
            lock (_blocked) { List<string> l = new List<string>(_blocked); l.Sort(StringComparer.OrdinalIgnoreCase); return l; }
        }

        public static int Count { get { lock (_blocked) return _blocked.Count; } }

        internal static void Apply(IEnumerable<string> blocked, bool blockingEnabled)
        {
            bool same;
            lock (_blocked)
            {
                var next = new HashSet<string>(_blocked.Comparer);
                if (blocked != null) foreach (string b in blocked) if (!string.IsNullOrEmpty(b)) next.Add(b);
                // Polled with the chat every 12 s; an unchanged list must not make every chat row rebuild.
                same = blockingEnabled == BlockingEnabled && next.SetEquals(_blocked);
                if (!same) { _blocked.Clear(); _blocked.UnionWith(next); }
            }
            if (!same) { BlockingEnabled = blockingEnabled; Version++; }
            KmhCacheEvents.Raise(Updated, "ChatModeration");
        }

        internal static void Clear()
        {
            lock (_blocked) _blocked.Clear();
            BlockingEnabled = true;
            Version++;
        }

        public static int Version { get; private set; }
    }
}
