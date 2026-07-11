using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Diagnostics
{
    // Mirrors KMH log lines to the server when player-enabled or server-requested; forces verbose while active.
    internal static class KmhDebugUplink
    {
        private const int    MaxQueued      = 500;
        private const int    LinesPerFlush  = 40;
        private const double FlushSeconds   = 5.0;

        private static readonly object _lock = new object();
        private static readonly Queue<string> _queue = new Queue<string>();
        private static DateTime _nextFlushUtc = DateTime.MinValue;
        private static int  _dropped;
        private static bool _forcedDebug;
        private static bool _sending;   // re-entry guard: our own sends log via KmhLog

        // Set from the server hello; cleared on disconnect (fresh hello re-sets it).
        public static bool ServerRequested { get; set; }

        public static bool Active
            => KmhDispatcher.IsKmhServer
               && (ServerRequested || KMHPatchMod.Settings?.RemoteDebugLogging == true);

        // Called from KmhLog's file sink with the already-formatted "time [LEVEL] message" line.
        public static void Enqueue(string formattedLine)
        {
            if (_sending || !Active || string.IsNullOrEmpty(formattedLine)) return;
            lock (_lock)
            {
                if (_queue.Count >= MaxQueued) { _queue.Dequeue(); _dropped++; }
                _queue.Enqueue(formattedLine);
            }
        }

        // Driven every frame by the main-thread pump; cheap no-op between flush windows.
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
            try { KmhDispatcher.Send(KmhProtocol.Kind.DebugLog, new DebugLogPush { Lines = batch }); }
            catch { /* next flush retries with new lines; old batch is gone by design */ }
            finally { _sending = false; }
        }
    }
}
