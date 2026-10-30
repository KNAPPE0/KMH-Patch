using System;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Account;
using KMHPatch.SubProtocol;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Global
{
    // The one round trip is to Global, not through the server it is addressed to.
    internal static class KmhJoinTicketClient
    {
        // The session a request is running for, so a new connection is never shut out by one still in flight.
        private static int _runningFor = -1;

        public static string LastResult = "";
        public static bool Verified;

        // SignedOut, Global's refusal code, or ServerRefused; empty while nothing has failed.
        public static string Problem { get; private set; } = "";
        public static string ServerRefusal { get; private set; } = "";
        public static string Dialled { get; private set; } = "";

        public const string SignedOut        = "signed_out";
        public const string Unreachable      = "unreachable";
        public const string ServerRefused    = "server";
        public const string EndpointMismatch = "KMH-SRV-321";

        private static int _session = -1;
        private static string _serverId = "";
        private static bool _ticketSent;
        private static int _failures;
        private static DateTime _nextTryUtc = DateTime.MaxValue;

        public static void Register()
            => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.JoinVerified, OnVerified);

        private static void OnVerified(KmhEnvelope env)
        {
            bool ok = env?.GetBool("ok") ?? false;
            string reason = env?.GetString("reason") ?? "";
            KmhMainThread.Post(() =>
            {
                // An older server answers a no-ticket notice as a failed ticket; only a ticket actually sent has a verdict.
                if (!_ticketSent) return;
                Verified = ok;
                LastResult = ok ? "verified" : reason;
                if (ok) { Problem = ""; ServerRefusal = ""; _failures = 0; _nextTryUtc = DateTime.MaxValue; return; }
                Problem = ServerRefused;
                ServerRefusal = reason;
                Failed();
                if (reason.Length > 0) KmhLog.Debug($"Global: this server did not verify the join ticket - {reason}");
            });
        }

        // The server is told which kind of unverified this is, so it never asks a signed-in player to sign in.
        public static void PresentAsync(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return;
            int session = KmhDispatcher.SessionGeneration;
            if (session != _session || !string.Equals(serverId, _serverId, StringComparison.Ordinal)) BeginSession(session, serverId);
            // Read now, from the connection itself: the server id is only what the far end claimed to be.
            Dialled = Catalog.CatalogPushGuard.CurrentEndpoint();
            string token = KmhAccountStore.TokenFor(GlobalHttp.BaseUrl);
            if (token.Length == 0)
            {
                Problem = SignedOut;
                _nextTryUtc = DateTime.MaxValue;
                TellServer(session, serverId, SignedOut);
                return;
            }
            if (Interlocked.Exchange(ref _runningFor, session) == session) return;
            string dialled = Dialled;

            Task.Run(async () =>
            {
                try { await RunAsync(serverId, dialled, session, token).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Global: join ticket request failed ({ex.GetType().Name})");
                    KmhMainThread.Post(() => { if (session == _session) { Problem = Unreachable; Failed(); } });
                }
                finally { Interlocked.CompareExchange(ref _runningFor, -1, session); }
            });
        }

        private static async Task RunAsync(string serverId, string dialled, int session, string token)
        {
            // Bound to the endpoint actually dialled, so a server claiming another's id gets nothing to replay there.
            string json = RequestBody(serverId, dialled);
            // Bound to the running Steam account so a credential copied onto another account cannot mint a ticket.
            ulong steam = Account.KmhSteamPersona.SteamId64();
            string path = "/api/v1/player/join-ticket" + (steam > 0 ? "?steam_id=" + steam : "");
            GlobalHttp.Result r = await GlobalHttp.PostAsync(path, json, token,
                                                             CancellationToken.None).ConfigureAwait(false);
            if (!r.Ok || r.Body == null)
            {
                string code = RefusalCode(r);
                KmhLog.Debug($"Global: no join ticket ({code})");
                KmhMainThread.Post(() =>
                {
                    if (!StillSameSession(session, serverId, _session, _serverId)) return;
                    Problem = code;
                    Failed();
                    TellServer(session, serverId, ServerClass(code, dialled));
                });
                return;
            }

            // Sent as-is (reshaping breaks the signature); dropped if the session moved on, or the next server gets it.
            KmhMainThread.Post(() =>
            {
                if (!StillSameSession(session, serverId, KmhDispatcher.SessionGeneration, KmhDispatcher.ServerId)) return;
                _ticketSent = true;
                KmhDispatcher.Send(KmhProtocol.Kind.JoinTicket, new { ticket = r.Body, unverified = "" });
            });
        }

        public static void Tick()
        {
            if (Verified || !KmhDispatcher.IsKmhServer || string.IsNullOrEmpty(_serverId)) return;
            if (Volatile.Read(ref _runningFor) != -1 || _session != KmhDispatcher.SessionGeneration) return;
            if (!RetryDue(Problem, KmhAccountStore.TokenFor(GlobalHttp.BaseUrl).Length > 0, DateTime.UtcNow, _nextTryUtc)) return;
            _nextTryUtc = DateTime.MaxValue;
            PresentAsync(_serverId);
        }

        internal static bool RetryDue(string problem, bool hasToken, DateTime nowUtc, DateTime nextTryUtc)
        {
            if (problem == SignedOut) return hasToken;
            if (string.IsNullOrEmpty(problem) || !hasToken) return false;
            return nowUtc >= nextTryUtc;
        }

        internal static TimeSpan Backoff(int failures)
        {
            int n = Math.Max(1, Math.Min(failures, 7));
            double s = Math.Min(600, 15 * Math.Pow(2, n - 1));
            return TimeSpan.FromSeconds(s);
        }

        private static void Failed()
        {
            _failures++;
            _nextTryUtc = DateTime.UtcNow + Backoff(_failures);
        }

        internal static string RefusalCode(GlobalHttp.Result r)
        {
            if (r == null || r.Unreachable) return Unreachable;
            if (!string.IsNullOrEmpty(r.ErrorCode)) return r.ErrorCode;
            return "http." + r.Status;
        }

        // What the server is told: only which kind of unverified this is, never the addresses or Global's answer.
        internal static string ServerClass(string problem, string dialled)
        {
            if (problem == SignedOut) return SignedOut;
            if (problem == EndpointMismatch) return IsLocalAddress(dialled) ? "local_address" : "other_address";
            return "unconfirmed";
        }

        // Posted, so a notice made while the hello is still being handled follows the ack rather than racing it.
        private static void TellServer(int session, string serverId, string unverified)
            => KmhMainThread.Post(() =>
            {
                if (!StillSameSession(session, serverId, KmhDispatcher.SessionGeneration, KmhDispatcher.ServerId)) return;
                KmhDispatcher.Send(KmhProtocol.Kind.JoinTicket, new { ticket = (object)null, unverified });
            });

        internal static string ProblemText(string problem, string serverRefusal, string dialled, string publicEndpoint)
        {
            switch (problem ?? "")
            {
                case "":
                    return "";
                case SignedOut:
                    return UI.KmhText.Get("KMH_Join_SignedOut",
                        "You are not signed in to Colony Nexus Global. Link Steam under Account & links - Colony Nexus then confirms you on this server without a reconnect.");
                case EndpointMismatch:
                    string pub = string.IsNullOrEmpty(publicEndpoint)
                        ? UI.KmhText.Get("KMH_Join_PublicUnknown", "the address the server browser lists")
                        : UI.KmhDisplayText.Inert(publicEndpoint, 200);
                    string at = UI.KmhDisplayText.Inert(string.IsNullOrEmpty(dialled) ? "?" : dialled, 200);
                    return IsLocalAddress(dialled)
                        ? UI.KmhText.Get("KMH_Join_LocalAddress",
                            "You connected through a local address ({0}). Global can only confirm your identity when you connect through the server's public address ({1}).", at, pub)
                        : UI.KmhText.Get("KMH_Join_OtherAddress",
                            "You connected through {0}, which is not the address this server registered with Colony Nexus Global ({1}), so Global cannot confirm your identity on this connection.", at, pub);
                case Unreachable:
                    return UI.KmhText.Get("KMH_Join_Unreachable",
                        "Colony Nexus Global could not be reached, so your identity on this server is not confirmed yet. Colony Nexus tries again on its own.");
                case ServerRefused:
                    return UI.KmhText.Get("KMH_Join_ServerRefused",
                        "This server did not accept your Colony Nexus Global ticket ({0}). Colony Nexus tries again on its own.",
                        UI.KmhDisplayText.Inert(string.IsNullOrEmpty(serverRefusal) ? "?" : serverRefusal, 200));
                default:
                    return UI.KmhText.Get("KMH_Join_Refused",
                        "You are signed in, but Colony Nexus Global did not confirm your identity on this server ({0}). Colony Nexus tries again on its own.",
                        UI.KmhDisplayText.Inert(problem, 64));
            }
        }

        public static string CurrentProblemText()
        {
            if (Verified) return "";
            string pub = KmhDirectoryClient.CardById(KmhDispatcher.ServerId)?.JoinEndpoint ?? "";
            return ProblemText(Problem, ServerRefusal, Dialled, pub);
        }

        // Display only, and literal: loopback, private, link-local and carrier-grade NAT ranges, and local names.
        internal static bool IsLocalAddress(string endpoint)
        {
            string host = (endpoint ?? "").Trim();
            int colon = host.LastIndexOf(':');
            if (colon > 0 && host.IndexOf(':') == colon) host = host.Substring(0, colon);
            else if (host.StartsWith("[", StringComparison.Ordinal) && host.IndexOf(']') > 0) host = host.Substring(1, host.IndexOf(']') - 1);
            host = host.Trim('[', ']');
            if (host.Length == 0) return false;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".home", StringComparison.OrdinalIgnoreCase)) return true;
            if (!System.Net.IPAddress.TryParse(host, out System.Net.IPAddress ip)) return false;
            if (System.Net.IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                byte[] v6 = ip.GetAddressBytes();
                return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (v6[0] & 0xFE) == 0xFC;
            }
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || b[0] == 127
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }

        private static void BeginSession(int session, string serverId)
        {
            _session = session; _serverId = serverId ?? "";
            Verified = false; LastResult = ""; Problem = ""; ServerRefusal = ""; Dialled = "";
            _ticketSent = false; _failures = 0; _nextTryUtc = DateTime.MaxValue;
        }

        internal static void ForgetSession()
        {
            BeginSession(-1, "");
            Volatile.Write(ref _runningFor, -1);
        }

        internal static string RequestBody(string serverId, string dialled)
            => new JObject { ["server_id"] = serverId, ["dialled"] = dialled ?? "" }.ToString(Newtonsoft.Json.Formatting.None);

        internal static bool StillSameSession(int askedIn, string askedFor, int nowIn, string nowFor)
            => askedIn == nowIn && string.Equals(askedFor, nowFor, StringComparison.Ordinal);

        internal static void ResetForTest() => ForgetSession();

        internal static void SetForTest(string problem, string serverRefusal, string dialled)
        { Problem = problem ?? ""; ServerRefusal = serverRefusal ?? ""; Dialled = dialled ?? ""; }
    }
}
