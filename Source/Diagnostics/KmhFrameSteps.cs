using System;
using System.Collections.Generic;

namespace KMHPatch.Diagnostics
{
    // Root.Update runs KMH's per-frame work: one step throwing must not stop the rest, nor report itself 60x a second.
    internal static class KmhFrameSteps
    {
        private static readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        // Delegates are held by the caller, so running a frame's steps allocates nothing.
        internal static void Run(string[] names, Action[] steps)
        {
            if (names == null || steps == null) return;
            for (int i = 0; i < steps.Length && i < names.Length; i++)
            {
                try { steps[i]?.Invoke(); }
                catch (Exception ex)
                {
                    if (_reported.Add(names[i]))
                        KmhLog.Warn($"KMH per-frame step '{names[i]}' threw and is reported once: {ex.Message}");
                }
            }
        }

        // A server switch is a fresh start, so a fault that only affected the last session is worth hearing about again.
        internal static void Clear() => _reported.Clear();
    }
}
