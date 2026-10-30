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
        internal const int MaxPerChannel = 500;
        private static readonly Dictionary<string, List<ChatMessage>> _byChannel = new Dictionary<string, List<ChatMessage>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> _lastReadId = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> _maxSeenId  = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int>  _unreadByChannel = new Dictionary<string, int>(StringComparer.Ordinal);
        private static volatile int _totalUnread;   // read lock-free by the per-frame tab glow

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
                // An unchanged poll must not bump the generation (full relayout); Updated still fires for the badge.
                if (!(_byChannel.TryGetValue(channel, out List<ChatMessage> had) && SameLines(had, list)))
                {
                    bool firstContact = had == null;
                    _byChannel[channel] = list;
                    long top = list.Count > 0 ? list[list.Count - 1].Id : 0;
                    if (top > Get(_maxSeenId, channel)) _maxSeenId[channel] = top;
                    // Only the very first sight of a channel counts as seen, or a refresh acknowledges what was never read.
                    if (firstContact) _lastReadId[channel] = top;
                    _generation++;
                    RecomputeUnreadLocked();
                }
            }
            KmhCacheEvents.Raise(Updated, "Chat");
        }

        internal static bool SameLines(List<ChatMessage> a, List<ChatMessage> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                ChatMessage x = a[i], y = b[i];
                if (x.Id != y.Id || x.SentUtcTicks != y.SentUtcTicks || x.IsVideo != y.IsVideo || x.SenderVerified != y.SenderVerified
                    || x.Body != y.Body || x.FromUsername != y.FromUsername || x.ImageUrl != y.ImageUrl || x.Origin != y.Origin
                    || x.MediaId != y.MediaId || x.MediaRef != y.MediaRef || x.Channel != y.Channel) return false;
            }
            return true;
        }

        // On the way in so no renderer can forget: Widgets.Label parses rich text and an unclosed tag reformats the panel.
        private static ChatMessage MakeInert(ChatMessage m)
        {
            if (m == null) return null;
            m.Body         = UI.KmhDisplayText.Inert(m.Body);
            m.FromUsername = UI.KmhDisplayText.Inert(m.FromUsername);
            m.Origin       = UI.KmhDisplayText.Inert(m.Origin);
            m.RelayedBy    = UI.KmhDisplayText.Inert(m.RelayedBy);
            // Fetched, played or opened by the row, so a media url that is not https never gets in.
            if (!string.IsNullOrEmpty(m.ImageUrl) && !ChatImageCache.IsWebMedia(m.ImageUrl)) { m.ImageUrl = ""; m.IsVideo = false; }
            return m;
        }

        public static void Append(ChatMessage m) => Append(m, notify: true);

        // notify:false is a history load: logged but not news, so backlog never arrives as live messages.
        internal static void Append(ChatMessage m, bool notify)
        {
            if (m == null || string.IsNullOrEmpty(m.Channel)) return;
            m = MakeInert(m);
            lock (_lock)
            {
                List<ChatMessage> list = Ring(m.Channel);
                foreach (ChatMessage e in list) if (e.Id == m.Id) return;
                bool inOrder = list.Count == 0 || list[list.Count - 1].Id < m.Id;
                list.Add(m);
                if (!inOrder) list.Sort((a, b) => a.Id.CompareTo(b.Id));
                // Global Chat only ever appends, and an open panel lays out every line a channel holds.
                if (list.Count > MaxPerChannel) list.RemoveRange(0, list.Count - MaxPerChannel);
                if (m.Id > Get(_maxSeenId, m.Channel)) _maxSeenId[m.Channel] = m.Id;
                // The first history load is backlog, seen the way a first snapshot is; only later lines are unread.
                if (!notify && m.Id > Get(_lastReadId, m.Channel)) _lastReadId[m.Channel] = m.Id;
                _generation++;
                RecomputeUnreadLocked();
            }
            KmhCacheEvents.Raise(Updated, "Chat");
            if (!notify) return;
            try { MessageArrived?.Invoke(m); } catch { /* a notifier must never poison the receive thread */ }
        }

        internal static int RemoveWhere(string channel, Predicate<ChatMessage> match)
        {
            if (string.IsNullOrEmpty(channel) || match == null) return 0;
            int removed;
            lock (_lock)
            {
                if (!_byChannel.TryGetValue(channel, out List<ChatMessage> list)) return 0;
                removed = list.RemoveAll(match);
                if (removed == 0) return 0;
                _generation++;
                RecomputeUnreadLocked();
            }
            KmhCacheEvents.Raise(Updated, "Chat");
            return removed;
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

        // Caller-owned buffer: the sidebar asks every frame.
        public static void ChannelsInto(List<string> into)
        {
            if (into == null) return;
            into.Clear();
            lock (_lock) foreach (string k in _byChannel.Keys) into.Add(k);
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

        // Server chat stays: it belongs to the session, which unlinking does not affect.
        internal static void ForgetGlobalChannels()
        {
            lock (_lock)
            {
                foreach (string channel in new List<string>(_byChannel.Keys))
                {
                    if (!ChatChannels.IsGlobal(channel) && !ChatChannels.IsGlobalDm(channel)) continue;
                    _byChannel.Remove(channel);
                    _lastReadId.Remove(channel);
                    _maxSeenId.Remove(channel);
                    _unreadByChannel.Remove(channel);
                }
                _generation++;
                RecomputeUnreadLocked();
            }
            KmhCacheEvents.Raise(Updated, "Chat");
        }
    }
}
