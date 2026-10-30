using System;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Global;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Account
{
    // Probed per launch, so a community Global without player linking stays quiet instead of a broken button.
    internal static class KmhGlobalCapabilities
    {
        public enum ProbeState { Unknown, Probing, Online, Offline }

        public static ProbeState State { get; private set; } = ProbeState.Unknown;
        public static bool PlayerLinking { get; private set; }
        public static bool GlobalChat { get; private set; }
        public static bool PublicSite { get; private set; }
        public static string Directory { get; private set; } = "";
        public static int Protocol { get; private set; }

        public static bool Online => State == ProbeState.Online;

        private static readonly object _lock = new object();

        // A briefly unreachable directory must not stay offline all launch, as probe-once-per-URL would leave it.
        internal static readonly TimeSpan RetryAfterOffline = TimeSpan.FromSeconds(60);
        private static DateTime _lastProbeUtc = DateTime.MinValue;

        internal static bool ShouldProbe(ProbeState state, bool sameDirectory, DateTime lastProbeUtc, DateTime nowUtc)
        {
            if (state == ProbeState.Probing) return false;
            if (!sameDirectory || state == ProbeState.Unknown) return true;
            return state == ProbeState.Offline && nowUtc - lastProbeUtc >= RetryAfterOffline;
        }

        public static void EnsureProbed()
        {
            lock (_lock)
            {
                if (!ShouldProbe(State, Directory == GlobalHttp.BaseUrl, _lastProbeUtc, DateTime.UtcNow)) return;
                State = ProbeState.Probing;
                Directory = GlobalHttp.BaseUrl;
                _lastProbeUtc = DateTime.UtcNow;
            }

            Task.Run(async () =>
            {
                try { await ProbeAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Global: capability probe failed: {ex.GetType().Name}");
                    State = ProbeState.Offline;
                }
            });
        }

        private static async Task ProbeAsync(CancellationToken ct)
        {
            GlobalHttp.Result r = await GlobalHttp.GetAsync("/api/v1/capabilities", ct).ConfigureAwait(false);
            if (r.Ok && r.Body != null)
            {
                Protocol = (int?)r.Body["protocol"] ?? 0;
                PlayerLinking = Has(r.Body["features"], "player-link");
                GlobalChat = Has(r.Body["features"], "global-chat");
                PublicSite = Has(r.Body["features"], "public-site");
                State = ProbeState.Online;
                return;
            }

            // A directory older than this endpoint still answers the listing, and its linking support is unknown.
            GlobalHttp.Result fallback = await GlobalHttp.GetAsync("/api/v1/directory/counters", ct).ConfigureAwait(false);
            PlayerLinking = false;
            GlobalChat = false;
            PublicSite = false;
            State = fallback.Ok ? ProbeState.Online : ProbeState.Offline;
        }

        private static bool Has(JToken features, string name)
        {
            if (features is not JArray arr) return false;
            foreach (JToken t in arr)
                if (string.Equals((string)t, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static void SeedForTest(ProbeState state, bool playerLinking, bool globalChat)
        {
            SeedForTest(state, playerLinking);
            GlobalChat = globalChat;
        }

        internal static void SeedForTest(ProbeState state, bool playerLinking)
        {
            State = state;
            PlayerLinking = playerLinking;
            Directory = GlobalHttp.BaseUrl;
        }

        internal static void ResetForTest()
        {
            State = ProbeState.Unknown;
            PlayerLinking = false;
            GlobalChat = false;
            PublicSite = false;
            Directory = "";
            Protocol = 0;
            _lastProbeUtc = DateTime.MinValue;
        }
    }
}
