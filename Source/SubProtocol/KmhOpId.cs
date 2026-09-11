using System;
using System.Collections.Generic;

namespace KMHPatch.SubProtocol
{
    // One id per outstanding action key, so a re-send or double-click reuses it while a deliberate repeat later mints a new one.
    public static class KmhOpId
    {
        private static readonly Dictionary<string, KeyValuePair<string, DateTime>> _open
            = new Dictionary<string, KeyValuePair<string, DateTime>>(StringComparer.Ordinal);

        // Covers a slow round trip without swallowing a deliberate repeat; the server's far longer window stays the authority.
        internal static readonly TimeSpan Hold = TimeSpan.FromSeconds(20);

        public static string For(string actionKey) => For(actionKey, DateTime.UtcNow);

        internal static string For(string actionKey, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(actionKey)) return Guid.NewGuid().ToString("N");
            lock (_open)
            {
                if (_open.TryGetValue(actionKey, out KeyValuePair<string, DateTime> held)
                    && nowUtc - held.Value < Hold)
                    return held.Key;

                string id = Guid.NewGuid().ToString("N");
                _open[actionKey] = new KeyValuePair<string, DateTime>(id, nowUtc);
                _byId[id] = actionKey;
                if (_open.Count > 256) Prune(nowUtc);
                return id;
            }
        }

        // The server reports a terminal result by op id, not by action key, so the reverse direction is kept too.
        private static readonly Dictionary<string, string> _byId = new Dictionary<string, string>(StringComparer.Ordinal);

        // The action reached a decision on the server. The next click is a new action, not a retry of this one.
        public static void SettledById(string opId)
        {
            if (string.IsNullOrEmpty(opId)) return;
            lock (_open)
            {
                if (!_byId.TryGetValue(opId, out string actionKey)) return;
                _byId.Remove(opId);
                if (_open.TryGetValue(actionKey, out KeyValuePair<string, DateTime> held) && held.Key == opId)
                    _open.Remove(actionKey);
            }
        }

        // Lets a caller say "already sent" instead of paying for a round trip the server will only refuse.
        public static bool IsInFlight(string actionKey) => IsInFlight(actionKey, DateTime.UtcNow);

        internal static bool IsInFlight(string actionKey, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(actionKey)) return false;
            lock (_open)
                return _open.TryGetValue(actionKey, out KeyValuePair<string, DateTime> held)
                    && nowUtc - held.Value < Hold;
        }

        // The action reached a conclusion, so the next click is a new one rather than a retry of this.
        public static void Settled(string actionKey)
        {
            if (string.IsNullOrEmpty(actionKey)) return;
            lock (_open)
            {
                if (_open.TryGetValue(actionKey, out KeyValuePair<string, DateTime> held)) _byId.Remove(held.Key);
                _open.Remove(actionKey);
            }
        }

        // Ids are meaningless to a different server, and the old ones must not outlive the session that made them.
        internal static void Clear()
        {
            lock (_open) { _open.Clear(); _byId.Clear(); }
        }

        private static void Prune(DateTime nowUtc)
        {
            var stale = new List<string>();
            foreach (KeyValuePair<string, KeyValuePair<string, DateTime>> kv in _open)
                if (nowUtc - kv.Value.Value >= Hold) stale.Add(kv.Key);
            foreach (string k in stale) { if (_open.TryGetValue(k, out var h)) _byId.Remove(h.Key); _open.Remove(k); }
        }

        internal static int OpenCount { get { lock (_open) return _open.Count; } }
    }
}
