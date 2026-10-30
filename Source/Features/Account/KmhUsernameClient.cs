using System;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Global;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Account
{
    internal enum UsernameState
    {
        Unknown,
        Loading,
        Ready,
        Saving,
        Failed,
        GlobalUnavailable,
    }

    // Display only: every server keys on the player global id, which never changes.
    internal static class KmhUsernameClient
    {
        private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(12);

        // Long enough that typing a handle is one lookup, not one per keystroke.
        internal const int CheckDelayTicks = 45;

        public static UsernameState State { get; private set; } = UsernameState.Unknown;
        public static string Username { get; private set; } = "";
        public static string Suggestion { get; private set; } = "";
        public static bool CanChange { get; private set; }
        // Releasing is free and always offered, so a player on a name not theirs need not wait out a rename cooldown.
        public static bool CanRelease { get; private set; }
        public static long ChangeAllowedAtUnix { get; private set; }
        public static string ErrorCode { get; private set; } = "";
        public static bool Busy { get; private set; }

        public static string Checked { get; private set; } = "";
        public static bool CheckedValid { get; private set; }
        public static bool CheckedAvailable { get; private set; }
        public static string CheckedReason { get; private set; } = "";
        public static bool Checking { get; private set; }

        private static string _pending = "";
        private static string _loadedFor = "";
        private static int _sinceTyped;
        private static CancellationTokenSource _checkCts;

        internal static int ChecksStarted { get; private set; }

        public static bool HasUsername => Username.Length > 0;

        public static string ChangeAllowedText()
        {
            if (ChangeAllowedAtUnix <= 0) return "";
            DateTimeOffset when = DateTimeOffset.FromUnixTimeSeconds(ChangeAllowedAtUnix).ToLocalTime();
            return when.ToString("d MMM yyyy");
        }

        public static void Refresh()
        {
            if (!KmhAccountStore.HasToken(GlobalHttp.BaseUrl))
            {
                Reset();
                return;
            }
            _loadedFor = GlobalHttp.BaseUrl;
            State = UsernameState.Loading;
            ErrorCode = "";
            Run(StatusAsync);
        }

        // One fetch per launch, so a screen that only wants to show the handle never polls for it.
        public static void EnsureLoaded()
        {
            if (Busy) return;
            if (State != UsernameState.Unknown && _loadedFor == GlobalHttp.BaseUrl) return;
            if (!KmhAccountStore.HasToken(GlobalHttp.BaseUrl)) return;
            Refresh();
        }

        public static void Set(string requested)
        {
            if (Busy) return;
            State = UsernameState.Saving;
            ErrorCode = "";
            string wanted = (requested ?? "").Trim();
            Run(ct => SetAsync(wanted, ct));
        }

        // An empty name is how Global is told to give the handle up; the same call answers with the new status.
        public static void Release() => Set("");

        public static void Typed(string text)
        {
            string wanted = (text ?? "").Trim();
            if (string.Equals(wanted, _pending, StringComparison.Ordinal)) return;
            _pending = wanted;
            _sinceTyped = 0;
            Checking = false;
            if (!string.Equals(wanted, Checked, StringComparison.OrdinalIgnoreCase)) ClearCheck();
        }

        public static void Tick()
        {
            if (_pending.Length == 0 || Checking) return;
            if (string.Equals(_pending, Checked, StringComparison.OrdinalIgnoreCase)) return;
            if (!KmhUsernameRules.LooksValid(_pending)) return;
            if (++_sinceTyped < CheckDelayTicks) return;
            _sinceTyped = 0;
            StartCheck(_pending);
        }

        private static void ClearCheck()
        {
            Checked = "";
            CheckedValid = false;
            CheckedAvailable = false;
            CheckedReason = "";
        }

        private static void StartCheck(string wanted)
        {
            try { _checkCts?.Cancel(); } catch (Exception) { }
            CancellationTokenSource cts = new CancellationTokenSource(CallTimeout);
            _checkCts = cts;
            Checking = true;
            ChecksStarted++;

            Task.Run(async () =>
            {
                try
                {
                    await CheckAsync(wanted, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { KmhLog.Debug($"Username: availability check failed: {ex.GetType().Name}"); }
                finally
                {
                    if (ReferenceEquals(_checkCts, cts)) { _checkCts = null; Checking = false; }
                    try { cts.Dispose(); } catch (Exception) { }
                }
            });
        }

        private static async Task CheckAsync(string wanted, CancellationToken ct)
        {
            string token = KmhAccountStore.TokenFor(GlobalHttp.BaseUrl);
            if (token.Length == 0) return;

            GlobalHttp.Result r = await GlobalHttp.GetAsync(
                "/api/v1/player/username/available?u=" + Uri.EscapeDataString(wanted), token, ct).ConfigureAwait(false);
            if (!r.Ok || r.Body == null || ct.IsCancellationRequested) return;

            Checked          = (string)r.Body["username"] ?? wanted;
            CheckedValid     = (bool?)r.Body["valid"] ?? false;
            CheckedAvailable = (bool?)r.Body["available"] ?? false;
            CheckedReason    = (string)r.Body["reason"] ?? "";
        }

        private static void Run(Func<CancellationToken, Task> work)
        {
            if (Busy) return;
            Busy = true;
            CancellationTokenSource cts = new CancellationTokenSource(CallTimeout);
            Task.Run(async () =>
            {
                try { await work(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { State = UsernameState.GlobalUnavailable; }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Username: call failed: {ex.GetType().Name}");
                    State = UsernameState.GlobalUnavailable;
                }
                finally
                {
                    Busy = false;
                    try { cts.Dispose(); } catch (Exception) { }
                }
            });
        }

        private static async Task StatusAsync(CancellationToken ct)
        {
            string token = KmhAccountStore.TokenFor(GlobalHttp.BaseUrl);
            if (token.Length == 0) { Reset(); return; }

            // Global cannot know this machine's Steam name, so the client offers it purely to seed the suggestion.
            string path = "/api/v1/player/username";
            if (KmhSteamPersona.Known) path += "?persona=" + Uri.EscapeDataString(KmhSteamPersona.Name);

            GlobalHttp.Result r = await GlobalHttp.GetAsync(path, token, ct).ConfigureAwait(false);
            if (!r.Ok || r.Body == null)
            {
                // 404 is a directory that predates usernames; that is not an error the player has to see.
                State = r.Status == 404 ? UsernameState.Ready : UsernameState.GlobalUnavailable;
                ErrorCode = r.Status == 404 ? "" : r.ErrorCode;
                return;
            }
            ApplyStatus(r.Body);
        }

        private static async Task SetAsync(string wanted, CancellationToken ct)
        {
            string token = KmhAccountStore.TokenFor(GlobalHttp.BaseUrl);
            if (token.Length == 0) { Reset(); return; }

            string json = JsonConvert.SerializeObject(new UsernameBody { username = wanted });
            GlobalHttp.Result r = await GlobalHttp.PostAsync("/api/v1/player/username", json, token, ct)
                                                  .ConfigureAwait(false);
            if (!r.Ok || r.Body == null)
            {
                ErrorCode = r.ErrorCode;
                State = r.Status == 0 ? UsernameState.GlobalUnavailable : UsernameState.Failed;
                ClearCheck();
                return;
            }

            ApplyStatus(r.Body);
            ClearCheck();
            _pending = "";
        }

        internal static void ApplyStatus(JObject body)
        {
            Username            = (string)body["username"] ?? "";
            Suggestion          = (string)body["suggestion"] ?? "";
            CanChange           = (bool?)body["can_change"] ?? false;
            // Absent from an older Global, where releasing did not exist: default to not offering it.
            CanRelease          = (bool?)body["can_release"] ?? false;
            ChangeAllowedAtUnix = (long?)body["change_allowed_at"] ?? 0;
            ErrorCode           = "";
            State               = UsernameState.Ready;
        }

        public static string Explain(string code)
        {
            switch (code)
            {
                case "KMH-USR-801": return "That username cannot be used.";
                case "KMH-USR-802": return "That username is already taken.";
                case "KMH-USR-803": return "You changed your username recently. Try again on " + ChangeAllowedText() + ".";
                case "KMH-LNK-705": return "Your Colony Nexus link is no longer valid. Link Steam again.";
                default:            return code.Length > 0 ? "Could not save that username (" + code + ")." : "";
            }
        }

        static KmhUsernameClient() => KmhIdentityTeardown.Register("username", Reset);

        public static void Reset()
        {
            State = UsernameState.Unknown;
            Username = Suggestion = ErrorCode = "";
            CanChange = false;
            ChangeAllowedAtUnix = 0;
            _pending = "";
            _loadedFor = "";
            _sinceTyped = 0;
            ClearCheck();
        }

        // The wire name is lowercase because Global's contract says so.
        private sealed class UsernameBody
        {
            public string username;
        }

        internal static void ResetForTest()
        {
            Reset();
            Busy = false;
            Checking = false;
            ChecksStarted = 0;
        }

        internal static void SeedForTest(string username, bool canChange, long changeAllowedAtUnix, string suggestion)
        {
            Username = username ?? "";
            CanChange = canChange;
            ChangeAllowedAtUnix = changeAllowedAtUnix;
            Suggestion = suggestion ?? "";
            State = UsernameState.Ready;
        }
    }
}
