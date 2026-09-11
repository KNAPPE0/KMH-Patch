using System;
using System.Collections.Generic;
using KMHPatch.Features.Chat.Dto;

namespace KMHPatch.Features.Chat
{
    // A channel's first snapshot counts as seen, so a reconnecting player is not handed a whole history as unread.
    public static class ChatCache
    {
        public const string ServerChannel = "server";

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, List<ChatMessage>> _byChannel = new Dictionary<string, List<ChatMessage>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> _lastReadId = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> _maxSeenId  = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int>  _unreadByChannel = new Dictionary<string, int>(StringComparer.Ordinal);
        private static volatile int _totalUnread;   // read lock-free by the per-frame tab glow

        // Bumped by every mutation, so a panel can tell "nothing changed" without copying the log to find out.
        private static long _generation;
        public static long Generation { get { lock (_lock) return _generation; } }

        public static event Action Updated;

        // Live messages only, never history loads, so a notification fires only for a genuinely new line.
        public static event Action<ChatMessage> MessageArrived;

        internal static void ApplySnapshot(string channel, List<ChatMessage> messages)
        {
            if (string.IsNullOrEmpty(channel)) return;
            lock (_lock)
            {
                List<ChatMessage> list = new List<ChatMessage>();
                if (messages != null) foreach (ChatMessage m in messages) if (m != null) list.Add(MakeInert(m));
                list.Sort((a, b) => a.Id.CompareTo(b.Id));
                bool firstContact = !_byChannel.ContainsKey(channel);
                _byChannel[channel] = list;
                long top = list.Count > 0 ? list[list.Count - 1].Id : 0;
                if (top > Get(_maxSeenId, channel)) _maxSeenId[channel] = top;
                // Only the very first sight of a channel counts as seen, or a refresh acknowledges what was never read.
                if (firstContact) _lastReadId[channel] = top;
                _generation++;
                RecomputeUnreadLocked();
            }
            KmhCacheEvents.Raise(Updated, "Chat");
        }

        // On the way in, so no renderer has to remember: Widgets.Label parses rich text, and an unclosed tag reformats the panel.
        private static ChatMessage MakeInert(ChatMessage m)
        {
            if (m == null) return null;
            m.Body         = UI.KmhDisplayText.Inert(m.Body);
            m.FromUsername = UI.KmhDisplayText.Inert(m.FromUsername);
            return m;
        }

        internal static void Append(ChatMessage m)
        {
            if (m == null || string.IsNullOrEmpty(m.Channel)) return;
            m = MakeInert(m);
            lock (_lock)
            {
                List<ChatMessage> list = Ring(m.Channel);
                foreach (ChatMessage e in list) if (e.Id == m.Id) return;   // dedup by id
                list.Add(m);
                list.Sort((a, b) => a.Id.CompareTo(b.Id));
                if (m.Id > Get(_maxSeenId, m.Channel)) _maxSeenId[m.Channel] = m.Id;
                _generation++;
                RecomputeUnreadLocked();
            }
            KmhCacheEvents.Raise(Updated, "Chat");
            try { MessageArrived?.Invoke(m); } catch { /* a notifier must never poison the receive thread */ }
        }

        public static List<ChatMessage> Recent(string channel)
        {
            lock (_lock) return _byChannel.TryGetValue(channel ?? "", out List<ChatMessage> l) ? new List<ChatMessage>(l) : new List<ChatMessage>();
        }

        public static bool HasChannel(string channel)
        {
            lock (_lock) return _byChannel.ContainsKey(channel ?? "");
        }

        public static List<string> Channels()
        {
            lock (_lock) return new List<string>(_byChannel.Keys);
        }

        // Maintained on change, not computed on read: the tab glow asks for this on every OnGUI pass.
        public static int Unread(string channel)
        {
            lock (_lock) return _unreadByChannel.TryGetValue(channel ?? "", out int n) ? n : 0;
        }

        public static int TotalUnread() => _totalUnread;

        private static void RecomputeUnreadLocked()
        {
            _unreadByChannel.Clear();
            int total = 0;
            foreach (KeyValuePair<string, List<ChatMessage>> kv in _byChannel)
            {
                long lastRead = Get(_lastReadId, kv.Key);
                int n = 0;
                foreach (ChatMessage m in kv.Value) if (m.Id > lastRead) n++;
                _unreadByChannel[kv.Key] = n;
                total += n;
            }
            _totalUnread = total;
        }

        public static void MarkRead(string channel)
        {
            bool changed = false;
            lock (_lock)
            {
                long max = Get(_maxSeenId, channel);
                if (Get(_lastReadId, channel) != max) { _lastReadId[channel ?? ""] = max; changed = true; RecomputeUnreadLocked(); }
            }
            if (changed) KmhCacheEvents.Raise(Updated, "Chat");
        }

        public static void MarkAllRead()
        {
            bool changed = false;
            lock (_lock)
            {
                foreach (string ch in new List<string>(_byChannel.Keys))
                {
                    long max = Get(_maxSeenId, ch);
                    if (Get(_lastReadId, ch) == max) continue;
                    _lastReadId[ch] = max;
                    changed = true;
                }
                if (changed) { _generation++; RecomputeUnreadLocked(); }
            }
            if (changed) KmhCacheEvents.Raise(Updated, "Chat");
        }

        private static List<ChatMessage> Ring(string channel)
        {
            if (!_byChannel.TryGetValue(channel, out List<ChatMessage> l)) { l = new List<ChatMessage>(); _byChannel[channel] = l; }
            return l;
        }

        private static long Get(Dictionary<string, long> d, string k) => d.TryGetValue(k ?? "", out long v) ? v : 0;

        internal static void Clear()
        {
            lock (_lock) { _byChannel.Clear(); _lastReadId.Clear(); _maxSeenId.Clear(); _unreadByChannel.Clear(); _totalUnread = 0; _generation++; }
        }
    }
}
