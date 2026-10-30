using System;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Account;
using KMHPatch.Features.Global;
using KMHPatch.SubProtocol;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Friends
{
    // Reported by the client because only the player token proves which GlobalUserId this is; Global checks the
    // ServerId against its directory, so a client cannot invent a server. Failures are silent; the next tick retries.
    internal static class KmhPresenceReporter
    {
        // Global treats a player as offline after three missed reports, so report comfortably inside that.
        internal static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(60);

        private static DateTime _nextReportUtc = DateTime.MinValue;
        // Whether anything is published on Global that turning presence off would have to go and clear.
        private static bool _published;
        private static string _lastReported = "";
        private static int _inFlight;

        private static string Directory => GlobalHttp.BaseUrl;
        private static string Token => KmhAccountStore.TokenFor(Directory);

        static KmhPresenceReporter() => KmhIdentityTeardown.Register("presence", Reset);

        public static void Reset()
        {
            _nextReportUtc = DateTime.MinValue;
            _published = false;
            _lastReported = "";
        }

        // The session changed: report now rather than up to a minute later, or a friend sees the wrong server.
        public static void Invalidate() => _nextReportUtc = DateTime.MinValue;

        public static void Tick()
        {
            try
            {
                if (Volatile.Read(ref _inFlight) != 0) return;
                if (Token.Length == 0) return;
                if (!KMHPatchMod.SettingsOrDefault.GlobalPresenceEnabled) { StopOnce(); return; }
                if (DateTime.UtcNow < _nextReportUtc) return;

                _nextReportUtc = DateTime.UtcNow.Add(ReportEvery);
                string serverId = KmhDispatcher.IsKmhServer ? KmhDispatcher.ServerId ?? "" : "";
                Report(serverId);
            }
            catch (Exception ex) { KmhLog.Debug($"Presence: tick failed ({ex.GetType().Name})."); }
        }

        private static void Report(string serverId)
        {
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
            _lastReported = serverId;
            _published = true;
            long seq = KmhFriendsClient.NextRead();
            Task.Run(async () =>
            {
                try
                {
                    var body = new JObject();
                    if (serverId.Length > 0) body["server_id"] = serverId;
                    GlobalHttp.Result r = await GlobalHttp.PostAsync("/api/v1/player/presence",
                        body.ToString(Newtonsoft.Json.Formatting.None), Token, CancellationToken.None).ConfigureAwait(false);
                    // The answer is this player's friends lists: the request badge and toast come from here.
                    if (r != null && r.Ok) KmhFriendsClient.ApplyPresence(r.Body, seq);
                }
                catch (Exception ex) { KmhLog.Debug($"Presence: report failed: {ex.Message}"); }
                finally { Interlocked.Exchange(ref _inFlight, 0); }
            });
        }

        internal static bool ReportForTest(string serverId)
        {
            bool published = _published;
            string last = _lastReported;
            Report(serverId ?? "");
            DateTime until = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref _inFlight) != 0 && DateTime.UtcNow < until) Thread.Sleep(5);
            _published = published;
            _lastReported = last;
            return Volatile.Read(ref _inFlight) == 0;
        }

        // Turning presence off clears what was already published rather than letting it age out over three minutes.
        private static void StopOnce()
        {
            if (!_published) return;
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
            _published = false;
            _lastReported = "";
            Task.Run(async () =>
            {
                try
                {
                    await GlobalHttp.DeleteAsync("/api/v1/player/presence", Token, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) { KmhLog.Debug($"Presence: clear failed: {ex.Message}"); }
                finally { Interlocked.Exchange(ref _inFlight, 0); }
            });
        }

        internal static string LastReportedForTest => _lastReported;
        internal static bool PublishedForTest => _published;
    }
}
