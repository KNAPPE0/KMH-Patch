using System;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Global;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Account
{
    internal enum AccountState
    {
        NotLinked,
        Loading,
        Linked,
        Opening,
        Waiting,
        Expired,
        Failed,
        AlreadyLinked,
        WrongSteamAccount,
        GlobalUnavailable,
    }

    // Nothing here asserts an identity: it asks Global for one and waits to be told.
    internal static class KmhAccountClient
    {
        internal const int PollIntervalTicks = 120;
        private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(12);

        public static AccountState State { get; private set; } = AccountState.NotLinked;
        public static string Code { get; private set; } = "";
        public static string LinkUrl { get; private set; } = "";
        public static long ExpiresAtUnix { get; private set; }
        public static string DisplayName { get; private set; } = "";
        public static string PlayerGlobalId { get; private set; } = "";
        public static string ErrorCode { get; private set; } = "";
        public static bool Busy { get; private set; }

        private static string _challengeId = "";
        private static CancellationTokenSource _cts;
        private static int _sinceLastPoll;

        public static string Directory => GlobalHttp.BaseUrl;

        public static string DeepLink => BuildDeepLink(LinkUrl, Code);

        // Same host as the issuing directory, and https unless both are loopback (Normalize's rule for the directory).
        internal static string DirectoryLinkUrl(string url, string directory)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri link)) return null;
            if (!Uri.TryCreate(directory ?? "", UriKind.Absolute, out Uri dir)) return null;
            if (!string.Equals(link.Host, dir.Host, StringComparison.OrdinalIgnoreCase)) return null;
            bool https = link.Scheme == Uri.UriSchemeHttps;
            bool localHttp = link.Scheme == Uri.UriSchemeHttp && GlobalHttp.IsLoopback(link.AbsoluteUri)
                          && GlobalHttp.IsLoopback(dir.AbsoluteUri);
            if (!https && !localHttp) return null;
            if (!string.IsNullOrEmpty(link.UserInfo)) return null;
            return link.AbsoluteUri;
        }

        internal static string BuildDeepLink(string url, string code)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            if (string.IsNullOrWhiteSpace(code)) return url;
            return url + (url.IndexOf('?') >= 0 ? "&" : "?") + "code=" + Uri.EscapeDataString(code.Trim());
        }

        public static long SecondsLeft
        {
            get
            {
                if (ExpiresAtUnix <= 0) return 0;
                long left = ExpiresAtUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return left > 0 ? left : 0;
            }
        }

        // A stored token is not trusted on sight; it may have been revoked from the web since.
        public static void Refresh()
        {
            Cancel();
            if (!KmhAccountStore.HasStoredToken(Directory))
            {
                State = AccountState.NotLinked;
                DisplayName = "";
                PlayerGlobalId = "";
                return;
            }

            DisplayName = KmhAccountStore.DisplayName;
            PlayerGlobalId = KmhAccountStore.PlayerGlobalId;
            State = AccountState.Loading;
            Run(MeAsync);
        }

        public static void StartLink()
        {
            Cancel();
            Code = "";
            LinkUrl = "";
            ExpiresAtUnix = 0;
            ErrorCode = "";
            State = AccountState.Opening;
            Run(StartAsync);
        }

        public static void Unlink()
        {
            Cancel();
            State = AccountState.Loading;
            Run(ct => UnlinkAsync(false, ct));
        }

        // Signs out every device: the only answer to a lost machine, whose credential a local unlink leaves working.
        public static void UnlinkEverywhere()
        {
            Cancel();
            State = AccountState.Loading;
            Run(ct => UnlinkAsync(true, ct));
        }

        // The dialog calls this on close, so an abandoned link attempt leaves nothing running.
        public static void Cancel()
        {
            try { _cts?.Cancel(); } catch (Exception) { }
            _cts = null;
            Busy = false;
            _challengeId = "";
            _sinceLastPoll = 0;
        }

        public static void CancelToIdle()
        {
            Cancel();
            State = KmhAccountStore.HasToken(Directory) ? AccountState.Linked : AccountState.NotLinked;
            Code = "";
            ExpiresAtUnix = 0;
        }

        // Driven from the dialog's own draw, so the poll stops the moment the window is gone.
        public static void Tick()
        {
            if (State != AccountState.Waiting) return;
            if (SecondsLeft <= 0) { Cancel(); State = AccountState.Expired; return; }
            if (Busy) return;
            if (++_sinceLastPoll < PollIntervalTicks) return;
            _sinceLastPoll = 0;
            Run(PollAsync);
        }

        private static void Run(Func<CancellationToken, Task> work)
        {
            if (Busy) return;
            Busy = true;
            CancellationTokenSource cts = new CancellationTokenSource(CallTimeout);
            _cts = cts;
            Task.Run(async () =>
            {
                try { await work(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Account: call failed: {ex.GetType().Name}");
                    Fail("KMH-NET-001");
                }
                finally
                {
                    Busy = false;
                    if (ReferenceEquals(_cts, cts)) _cts = null;
                    try { cts.Dispose(); } catch (Exception) { }
                }
            });
        }

        private static void Fail(string code)
        {
            ErrorCode = code ?? "";
            State = code == "KMH-NET-001" ? AccountState.GlobalUnavailable : AccountState.Failed;
        }

        private static async Task StartAsync(CancellationToken ct)
        {
            // Global binds the code to this Steam account, so a browser signed in as another cannot take the link.
            ulong steam = KmhSteamPersona.SteamId64();
            string body = "{\"steam_id\":\"" + steam.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}";

            GlobalHttp.Result r = await GlobalHttp.PostAsync("/api/v1/player-links", body, null, ct).ConfigureAwait(false);
            if (!r.Ok || r.Body == null) { Fail(r.ErrorCode); return; }

            // Opened by the shell without another click, so a substituted directory must not name anything else.
            string link = DirectoryLinkUrl((string)r.Body["link_url"], Directory);
            if (link == null) { Fail("KMH-SEC-503"); return; }

            _challengeId  = (string)r.Body["challenge_id"] ?? "";
            Code          = (string)r.Body["code"] ?? "";
            LinkUrl       = link;
            ExpiresAtUnix = (long?)r.Body["expires_at"] ?? 0;
            if (_challengeId.Length == 0 || Code.Length == 0) { Fail("KMH-SEC-503"); return; }

            _sinceLastPoll = 0;
            State = AccountState.Waiting;
        }

        private static async Task PollAsync(CancellationToken ct)
        {
            string id = _challengeId;
            if (id.Length == 0) return;

            GlobalHttp.Result r = await GlobalHttp.PostAsync(
                "/api/v1/player-links/" + Uri.EscapeDataString(id) + "/poll", null, null, ct).ConfigureAwait(false);

            // A poll that cannot reach Global is not a failed link: the code is still good until it expires.
            if (!r.Ok || r.Body == null)
            {
                if (r.Status == 404 || r.Status == 400) { Cancel(); State = AccountState.Expired; }
                return;
            }
            Apply(r.Body);
        }

        // State names are Global's; the token arrives exactly once.
        internal static void Apply(JObject body)
        {
            string state = (string)body["state"] ?? "";
            switch (state)
            {
                case "Pending":
                    return;
                case "Expired":
                    Cancel();
                    State = AccountState.Expired;
                    return;
                case "Completed":
                    return;
                case "Consumed":
                    break;
                default:
                    Fail("KMH-SEC-503");
                    return;
            }

            string token = (string)body["player_token"] ?? "";
            string who   = (string)body["player_global_id"] ?? "";
            string name  = (string)body["display_name"] ?? "";

            // Consumed with no token means another copy of the game already took it; this one is not linked.
            if (token.Length == 0 || who.Length == 0)
            {
                Cancel();
                State = AccountState.AlreadyLinked;
                return;
            }

            KmhAccountStore.Save(Directory, token, who, name);
            PlayerGlobalId = who;
            DisplayName = name;
            Cancel();
            State = AccountState.Linked;
        }

        private static async Task MeAsync(CancellationToken ct)
        {
            string token = KmhAccountStore.StoredTokenFor(Directory);
            if (token.Length == 0) { State = AccountState.NotLinked; return; }

            // Global checks this copy's Steam account against the token owner's; otherwise a credential left behind
            // after switching Steam accounts keeps reporting the old player.
            ulong steam = KmhSteamPersona.SteamId64();
            ApplyMe(await GlobalHttp.GetAsync(MePath(steam), token, ct).ConfigureAwait(false), steam);
        }

        private static string MePath(ulong steam) => "/api/v1/player/me" + (steam > 0 ? "?steam_id=" + steam : "");

        private static int _checking;
        private static int _checkFailures;
        private static DateTime _nextCheckUtc = DateTime.MinValue;

        // Per frame: a credential unconfirmed for this Steam account is not used until Global confirms it.
        public static void Watch()
        {
            ulong steam = KmhSteamPersona.SteamId64();
            if (!KmhAccountStore.NeedsSteamCheck(Directory, steam)) return;
            if (Busy || DateTime.UtcNow < _nextCheckUtc) return;
            if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0) return;

            string token = KmhAccountStore.StoredTokenFor(Directory);
            Task.Run(async () =>
            {
                try
                {
                    using (var cts = new CancellationTokenSource(CallTimeout))
                    {
                        GlobalHttp.Result r = await GlobalHttp.GetAsync(MePath(steam), token, cts.Token).ConfigureAwait(false);
                        ApplyMe(r, steam);
                        if (r.Ok || r.Status == 401 || r.Status == 409) _checkFailures = 0;
                        else _nextCheckUtc = DateTime.UtcNow + (r.RetryAfter > TimeSpan.Zero
                            ? r.RetryAfter : KmhRealtimeBackoff.For(_checkFailures++));
                    }
                }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Account: the Steam check failed: {ex.GetType().Name}");
                    _nextCheckUtc = DateTime.UtcNow + KmhRealtimeBackoff.For(_checkFailures++);
                }
                finally { Interlocked.Exchange(ref _checking, 0); }
            });
        }

        internal static bool CheckingForTest => Volatile.Read(ref _checking) != 0;

        internal static void ApplyMe(GlobalHttp.Result r) => ApplyMe(r, KmhSteamPersona.SteamId64());

        internal static void ApplyMe(GlobalHttp.Result r, ulong steam)
        {
            if (!r.Ok || r.Body == null)
            {
                // The identity changed while this was in flight, so the answer decides nothing about this one.
                if (r.ErrorCode == GlobalHttp.IdentityChangedCode) { SettleFromStore(); return; }
                // Another Steam account's credential: kept for its owner, never used here; their loaded state goes.
                if (r.Status == 409)
                {
                    State = AccountState.WrongSteamAccount;
                    ErrorCode = r.ErrorCode;
                    if (KmhAccountStore.RefuseSteam(steam)) KmhIdentityTeardown.ForgetIdentity();
                    return;
                }
                // 401 is the one answer that means the stored credential is genuinely gone, not that Global is down.
                if (r.Status == 401)
                {
                    KmhAccountStore.Clear();
                    PlayerGlobalId = "";
                    DisplayName = "";
                    State = AccountState.NotLinked;
                    ErrorCode = r.ErrorCode;
                }
                else State = AccountState.GlobalUnavailable;
                return;
            }

            PlayerGlobalId = (string)r.Body["player_global_id"] ?? "";
            DisplayName    = (string)r.Body["display_name"] ?? "";
            KmhAccountStore.ConfirmSteam(steam);
            KmhAccountStore.UpdateDisplay(DisplayName);
            State = AccountState.Linked;
        }

        private static void SettleFromStore()
        {
            bool held = KmhAccountStore.HasStoredToken(Directory);
            DisplayName = held ? KmhAccountStore.DisplayName : "";
            PlayerGlobalId = held ? KmhAccountStore.PlayerGlobalId : "";
            State = held ? AccountState.Linked : AccountState.NotLinked;
        }

        // Global being unreachable says nothing about a link already held, so trying again re-checks it.
        internal static bool RetryIsRelink => !KmhAccountStore.HasStoredToken(Directory);

        private static async Task UnlinkAsync(bool everywhere, CancellationToken ct)
        {
            string token = KmhAccountStore.StoredTokenFor(Directory);
            if (token.Length == 0) { State = AccountState.NotLinked; return; }

            string path = everywhere ? "/api/v1/player/unlink?all=1" : "/api/v1/player/unlink";
            GlobalHttp.Result r = await GlobalHttp.PostAsync(path, null, token, ct).ConfigureAwait(false);
            // A refused unlink still clears the local copy only when Global says the credential is no longer valid.
            if (!r.Ok && r.Status != 401) { State = AccountState.GlobalUnavailable; ErrorCode = r.ErrorCode; return; }

            // The store runs the identity teardown, which every per-account cache registers with.
            KmhAccountStore.Clear();
            PlayerGlobalId = "";
            DisplayName = "";
            ErrorCode = "";
            State = AccountState.NotLinked;
        }

        internal static void ResetForTest()
        {
            Cancel();
            _checkFailures = 0;
            _nextCheckUtc = DateTime.MinValue;
            State = AccountState.NotLinked;
            Code = LinkUrl = DisplayName = PlayerGlobalId = ErrorCode = "";
            ExpiresAtUnix = 0;
        }

        internal static void SeedWaitingForTest(string challengeId, long expiresAtUnix)
        {
            _challengeId = challengeId;
            ExpiresAtUnix = expiresAtUnix;
            State = AccountState.Waiting;
        }
    }
}
