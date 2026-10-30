using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace KMHPatch.Diagnostics
{
    // Colony Nexus work holding the game thread past a frame is a stutter; the log names it, once a minute per name.
    internal static class KmhSlowWork
    {
        internal const double SlowMs = 25.0;
        internal static readonly long SlowTicks = (long)(SlowMs * Stopwatch.Frequency / 1000.0);
        internal static readonly TimeSpan Quiet = TimeSpan.FromMinutes(1);
        private const int MaxNames = 256;

        private static readonly Dictionary<string, DateTime> _lastLogged = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        internal static void Note(string what, long elapsedTicks)
        {
            double ms = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            if (ms < SlowMs || !KmhLog.DebugEnabled) return;
            if (ShouldLog(what, ms, DateTime.UtcNow))
                KmhLog.Debug($"Perf: {what} held the game thread for {ms:0} ms (a 60 fps frame is 17 ms).");
        }

        internal static bool ShouldLog(string what, double ms, DateTime now)
        {
            if (ms < SlowMs || string.IsNullOrEmpty(what)) return false;
            lock (_lastLogged)
            {
                if (_lastLogged.TryGetValue(what, out DateTime last) && now - last < Quiet) return false;
                if (_lastLogged.Count >= MaxNames) _lastLogged.Clear();
                _lastLogged[what] = now;
                return true;
            }
        }

        internal static void ResetForTest() { lock (_lastLogged) _lastLogged.Clear(); }
    }
}
