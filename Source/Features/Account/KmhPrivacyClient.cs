using System;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Global;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Account
{
    internal enum PrivacyState { Unknown, Loading, Loaded, Saving, Unavailable }

    // Global decides; defaults mirror Global's conservative ones, so a page opened while Global is down never
    // suggests the player is more visible than they are.
    internal static class KmhPrivacyClient
    {
        public const string Nobody = "Nobody", Friends = "Friends", Everyone = "Everyone";

        public static PrivacyState State { get; private set; } = PrivacyState.Unknown;
        public static string ErrorCode { get; private set; } = "";

        public static string PresenceVisibility { get; private set; } = Friends;
        public static bool   ShowServer { get; private set; } = true;
        public static string FriendRequests { get; private set; } = Everyone;
        public static string DirectMessages { get; private set; } = Friends;

        // Global's default: on, since it only matches existing Steam friends who have it on too.
        public static bool   SteamDiscovery { get; private set; } = true;

        // Only the slot: this client's retry timing is its own, not the shared interval backoff.
        private static readonly Global.KmhPollGate _gate = Global.KmhPollGate.SlotOnly();
        public static bool Busy => _gate.Busy;

        private static string Directory => GlobalHttp.BaseUrl;

        static KmhPrivacyClient() => KmhIdentityTeardown.Register("privacy", Reset);

        public static void Reset()
        {
            State = PrivacyState.Unknown;
            ErrorCode = "";
            PresenceVisibility = Friends; ShowServer = true; FriendRequests = Everyone; DirectMessages = Friends;
            SteamDiscovery = true;
        }

        // Re-read while open: a once-per-launch copy hid another session's changes, then overwrote them on save.
        private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(20);
        private static DateTime _lastLoadUtc;

        // Unknown and Unavailable loop back on themselves and this runs every frame, so the floor stops a request per frame.
        private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(5);
        private static DateTime _nextRetryUtc = DateTime.MinValue;

        internal static Func<CancellationToken, Task> LoadForTest;
        internal static void SetStateForTest(PrivacyState s) => State = s;
        internal static void ResetRetryForTest() => _nextRetryUtc = DateTime.MinValue;

        public static void RefreshIfNeeded()
        {
            if (Busy) return;
            if (State == PrivacyState.Unknown || State == PrivacyState.Unavailable)
            {
                if (DateTime.UtcNow < _nextRetryUtc) return;
                _nextRetryUtc = DateTime.UtcNow + RetryEvery;
                Load();
                return;
            }
            if (_lastLoadUtc == default || DateTime.UtcNow - _lastLoadUtc >= RefreshEvery) Load();
        }

        public static void Load()
        {
            if (!_gate.TryEnter()) return;
            State = PrivacyState.Loading;
            Task.Run(async () =>
            {
                try { await (LoadForTest ?? LoadAsync)(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { KmhLog.Debug($"Privacy: load failed: {ex.Message}"); State = PrivacyState.Unavailable; }
                finally { _gate.Exit(); }
            });
        }

        // One field per call: sending all five let a stale session revert another device's changes, even re-granting
        // withdrawn Steam-discovery consent. Applied locally only after Global accepts.
        public static void SetPresenceVisibility(string v) => Patch(new JObject { ["presence_visibility"] = v });
        public static void SetShowServer(bool v)           => Patch(new JObject { ["show_server"] = v });
        public static void SetFriendRequests(string v)     => Patch(new JObject { ["friend_requests"] = v });
        public static void SetDirectMessages(string v)     => Patch(new JObject { ["direct_messages"] = v });
        public static void SetSteamDiscovery(bool v)       => Patch(new JObject { ["steam_discovery"] = v });

        private static void Patch(JObject body)
        {
            if (!_gate.TryEnter()) return;
            State = PrivacyState.Saving;
            Task.Run(async () =>
            {
                try { await SaveAsync(body, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { KmhLog.Debug($"Privacy: save failed: {ex.Message}"); State = PrivacyState.Unavailable; }
                finally { _gate.Exit(); }
            });
        }

        private static async Task LoadAsync(CancellationToken ct)
        {
            // GlobalHttp refuses an answer arriving after sign-out (it captures the identity per bearer call);
            // a second check here could be lost without any test noticing.
            string token = KmhAccountStore.TokenFor(Directory);
            if (token.Length == 0) { Reset(); return; }

            GlobalHttp.Result r = await GlobalHttp.GetAsync("/api/v1/player/privacy", token, ct).ConfigureAwait(false);
            if (!r.Ok || r.Body == null) { State = PrivacyState.Unavailable; ErrorCode = r.ErrorCode; return; }
            Apply(r.Body);
        }

        private static async Task SaveAsync(JObject body, CancellationToken ct)
        {
            string token = KmhAccountStore.TokenFor(Directory);
            if (token.Length == 0) { Reset(); return; }

            GlobalHttp.Result r = await GlobalHttp.PostAsync("/api/v1/player/privacy", body.ToString(Newtonsoft.Json.Formatting.None),
                                                            token, ct).ConfigureAwait(false);
            if (!r.Ok || r.Body == null) { State = PrivacyState.Unavailable; ErrorCode = r.ErrorCode; return; }
            Apply(r.Body);
        }

        // Read back from what Global returned, never from what was sent.
        private static void Apply(JObject body)
        {
            PresenceVisibility = Audience((string)body["presence_visibility"], Friends);
            ShowServer         = (bool?)body["show_server"] ?? true;
            FriendRequests     = (string)body["friend_requests"] == Nobody ? Nobody : Everyone;
            DirectMessages     = Audience((string)body["direct_messages"], Friends);
            // Absent on an older Global with no discovery at all, where on matches nobody and changes nothing.
            SteamDiscovery     = (bool?)body["steam_discovery"] ?? true;
            ErrorCode = "";
            State = PrivacyState.Loaded;
            _lastLoadUtc = DateTime.UtcNow;
        }

        // An audience this build does not know reads as the safer of the two neighbours, never as Everyone.
        internal static string Audience(string value, string fallback)
            => value == Nobody || value == Friends || value == Everyone ? value : fallback;
    }
}
