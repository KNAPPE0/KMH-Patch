using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace KMHPatch.Diagnostics
{
    // Root.Update runs KMH's per-frame work: one step throwing must not stop the rest, nor report itself 60x a second.
    internal static class KmhFrameSteps
    {
        private static readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        // Delegates are held by the caller, so running a frame's steps allocates nothing.
        internal static void Run(string[] names, Action[] steps) => Run(names, steps, null);

        internal static void Run(string[] names, Action[] steps, long[] timings)
        {
            if (names == null || steps == null) return;
            for (int i = 0; i < steps.Length && i < names.Length; i++)
            {
                long started = timings != null ? Stopwatch.GetTimestamp() : 0L;
                try { steps[i]?.Invoke(); }
                catch (Exception ex)
                {
                    if (_reported.Add(names[i]))
                        KmhLog.Warn($"KMH per-frame step '{names[i]}' threw and is reported once: {ex.Message}");
                }
                if (timings != null && i < timings.Length) timings[i] = Stopwatch.GetTimestamp() - started;
            }
        }

        // A server switch is a fresh start, so a fault that only affected the last session is worth hearing about again.
        internal static void Clear() => _reported.Clear();
    }
}
