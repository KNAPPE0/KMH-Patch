using System.Collections.Generic;

namespace KMHPatch.Features.Comms
{
    // Counted per channel, not a single "current" one: the hub and the pop-out can both show chat and close independently.
    internal static class CommsFocus
    {
        private static readonly Dictionary<string, int> _viewers = new Dictionary<string, int>(System.StringComparer.Ordinal);

        internal static void Enter(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            lock (_viewers) _viewers[channel] = (_viewers.TryGetValue(channel, out int n) ? n : 0) + 1;
        }

        internal static void Leave(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            lock (_viewers)
            {
                if (!_viewers.TryGetValue(channel, out int n)) return;
                if (n <= 1) _viewers.Remove(channel); else _viewers[channel] = n - 1;
            }
        }

        internal static bool IsViewing(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return false;
            lock (_viewers) return _viewers.ContainsKey(channel);
        }

        // A window that switched channels: one call, so a mismatched pair can never leave a stale viewer behind.
        internal static void Switch(ref string held, string next)
        {
            if (string.Equals(held, next, System.StringComparison.Ordinal)) return;
            Leave(held);
            held = next;
            Enter(held);
        }

        internal static void Clear() { lock (_viewers) _viewers.Clear(); }

        internal static int ViewerCount(string channel)
        {
            lock (_viewers) return _viewers.TryGetValue(channel ?? "", out int n) ? n : 0;
        }
    }
}
