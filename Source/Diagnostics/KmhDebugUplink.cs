using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Diagnostics
{
    // Consent is local and absolute: the server's flag only raises a prompt, and it covers KMH log lines only.
    internal static class KmhDebugUplink
    {
        private const int    MaxQueued        = 500;
        private const int    LinesPerFlush    = 40;
        private const double FlushSeconds     = 5.0;
        internal const int   MaxLineBytes     = 2000;
        internal const long  MaxSessionBytes  = 8L * 1024 * 1024;

        private static long _sessionBytes;

        private static readonly object _lock = new object();
        private static readonly Queue<string> _queue = new Queue<string>();
        private static DateTime _nextFlushUtc = DateTime.MinValue;
        private static long     _sequence;   // lets the server spot a missing or out-of-order batch
        private static int  _dropped;
        private static bool _forcedDebug;
        private static bool _sending;   // re-entry guard: our own sends log via KmhLog

        // A request only, never a grant.
        public static bool ServerRequested { get; set; }

        // Scoped to the current server and cleared on disconnect, so consent never follows the player elsewhere.
        public static bool SessionConsent { get; private set; }

        private static bool _prompted;

        // The three-way choice was written by the settings screens and read by nobody, so "Never" still got asked.
        private static int Mode
            => KMHPatchMod.Settings?.LogSharingMode ?? KMHPatchSettings.LogShareAsk;

        public static bool Active
            => KmhDispatcher.IsKmhServer
               && Mode != KMHPatchSettings.LogShareNever
               && (SessionConsent || KMHPatchMod.Settings?.ShareDebugLogsWithServer == true);

        public static bool AwaitingConsent
            => KmhDispatcher.IsKmhServer && ServerRequested && !_prompted && !Active
               && Mode == KMHPatchSettings.LogShareAsk;

        public static void GrantForSession()
        {
            SessionConsent = true;
            _prompted      = true;
            KmhLog.Info("Debug uplink: sharing KMH log with this server for this session (player consent).");
        }

        public static void DeclineForSession()
        {
            _prompted = true;
            KmhLog.Info("Debug uplink: declined for this server.");
        }

        // The immediate stop from Advanced & tools. Also clears the standing opt-in, or the next pump would restart it.
        public static void StopSharing()
        {
            SessionConsent = false;
            _prompted      = true;
            if (KMHPatchMod.Settings != null)
            {
                KMHPatchMod.Settings.ShareDebugLogsWithServer = false;
                KMHPatchMod.SaveSettings();
            }
            lock (_lock) { _queue.Clear(); _dropped = 0; }
            KmhLog.Info("Debug uplink: stopped by the player. Nothing further is sent.");
        }

        public static void ResetForNewServer()
        {
            ServerRequested = false;
            SessionConsent  = false;
            _prompted       = false;
            lock (_lock) { _queue.Clear(); _dropped = 0; _sequence = 0; _sessionBytes = 0; }
        }

        public static void Enqueue(string formattedLine)
        {
            if (_sending || !Active || string.IsNullOrEmpty(formattedLine)) return;
            // Bytes, because the caps are about what goes on the wire and one emoji is four of them.
            string line = TrimToBytes(formattedLine, MaxLineBytes);
            lock (_lock)
            {
                if (_sessionBytes >= MaxSessionBytes) { _dropped++; return; }
                if (_queue.Count >= MaxQueued) { _sessionBytes -= Utf8Bytes(_queue.Dequeue()); _dropped++; }
                _queue.Enqueue(line);
                _sessionBytes += Utf8Bytes(line);
            }
        }

        private static int Utf8Bytes(string s)
            => string.IsNullOrEmpty(s) ? 0 : System.Text.Encoding.UTF8.GetByteCount(s);

        // Cuts on a character boundary, so a multi-byte codepoint is never split into invalid UTF-8.
        private static string TrimToBytes(string s, int maxBytes)
        {
            if (Utf8Bytes(s) <= maxBytes) return s;
            int lo = 0, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (System.Text.Encoding.UTF8.GetByteCount(s.ToCharArray(), 0, mid) <= maxBytes) lo = mid; else hi = mid - 1;
            }
            if (lo > 0 && char.IsHighSurrogate(s[lo - 1])) lo--;
            return s.Substring(0, lo) + "…";
        }

        public static void Pump()
        {
            bool active = Active;

            // Force verbose logging on while active so "everything we log" actually exists to send.
            if (active && !KmhLog.DebugEnabled) { KmhLog.DebugEnabled = true; _forcedDebug = true; }
            else if (!active && _forcedDebug)
            {
                _forcedDebug = false;
                KmhLog.DebugEnabled = KMHPatchMod.Settings?.DebugLogging == true;
            }

            if (!active) return;
            DateTime now = DateTime.UtcNow;
            if (now < _nextFlushUtc) return;
            _nextFlushUtc = now.AddSeconds(FlushSeconds);

            List<string> batch = null;
            lock (_lock)
            {
                if (_queue.Count == 0) return;
                batch = new List<string>(Math.Min(_queue.Count, LinesPerFlush));
                while (_queue.Count > 0 && batch.Count < LinesPerFlush) batch.Add(_queue.Dequeue());
                if (_dropped > 0) { batch.Add($"-- uplink queue overflowed; {_dropped} line(s) dropped --"); _dropped = 0; }
            }

            _sending = true;
            try
            {
                KmhDispatcher.Send(KmhProtocol.Kind.DebugLog, new DebugLogPush
                { Lines = batch, SessionId = KmhLog.SessionId, Sequence = ++_sequence });
            }
            catch { /* next flush retries with new lines; old batch is gone by design */ }
            finally { _sending = false; }
        }
    }
}
