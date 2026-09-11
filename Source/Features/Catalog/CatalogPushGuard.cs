using System;
using System.Collections.Generic;

namespace KMHPatch.Features.Catalog
{
    // A modpack cannot change without a restart, so a reconnect need not re-stream a catalog; one guard for every pusher.
    internal static class CatalogPushGuard
    {
        internal static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

        private sealed class Sent { internal string Endpoint = ""; internal DateTime Utc; }
        private static readonly Dictionary<string, Sent> _byKind = new Dictionary<string, Sent>(StringComparer.Ordinal);

        // An unknown endpoint skips the throttle and always pushes: a guess would withhold a catalog the server needs.
        internal static bool AlreadySent(string kind, string endpoint, out int secondsAgo)
            => AlreadySent(kind, endpoint, DateTime.UtcNow, out secondsAgo);

        internal static bool AlreadySent(string kind, string endpoint, DateTime now, out int secondsAgo)
        {
            secondsAgo = 0;
            if (string.IsNullOrEmpty(endpoint) || !_byKind.TryGetValue(kind ?? "", out Sent s)) return false;
            if (!string.Equals(endpoint, s.Endpoint, StringComparison.OrdinalIgnoreCase)) return false;
            TimeSpan age = now - s.Utc;
            if (age >= Window) return false;
            secondsAgo = (int)age.TotalSeconds;
            return true;
        }

        // Recorded only after a COMPLETE push, so a half-sent catalog is sent again rather than latched as done.
        internal static void MarkSent(string kind, string endpoint) => MarkSent(kind, endpoint, DateTime.UtcNow);

        internal static void MarkSent(string kind, string endpoint, DateTime now)
        {
            if (string.IsNullOrEmpty(kind)) return;
            _byKind[kind] = new Sent { Endpoint = endpoint ?? "", Utc = now };
        }

        // A different server, or one that says it holds a different catalog, has to be told again.
        internal static void Forget(string kind)
        {
            if (!string.IsNullOrEmpty(kind)) _byKind.Remove(kind);
        }

        internal static void ForgetAll() => _byKind.Clear();

        // Reported as host:port, or empty when RWT has not settled on one yet.
        internal static string CurrentEndpoint()
        {
            try { return $"{TCPNetwork.Network.Ip}:{TCPNetwork.Network.Port}"; }
            catch { return ""; }
        }
    }
}
