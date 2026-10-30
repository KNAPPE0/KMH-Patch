using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Account;
using KMHPatch.Features.Global;
using KMHPatch.UI;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Friends
{
    internal enum SteamDiscoveryState { Idle, Running, Done, Unavailable, Refused, NoSteam, Rejected }

    // Global only befriends where the other side named this player too; a one-sided match tells nobody.
    internal static class KmhSteamDiscoveryClient
    {
        public static SteamDiscoveryState State { get; private set; } = SteamDiscoveryState.Idle;
        public static string ErrorCode { get; private set; } = "";
        public static int LastMatched { get; private set; }
        public static int LastAdded { get; private set; }
        public static DateTime LastRunUtc { get; private set; }

        // Only the slot: this client's retry timing is its own, not the shared interval backoff.
        private static readonly Global.KmhPollGate _gate = Global.KmhPollGate.SlotOnly();
        public static bool Busy => _gate.Busy;

        private static string Directory => GlobalHttp.BaseUrl;
        private static string Token => KmhAccountStore.TokenFor(Directory);

        // Each run is a round trip plus a Global write and Steam lists change slowly: once per launch, never on a timer.
        private static bool _ranThisLaunch;

        public static bool Offered => KmhSteamFriends.Available && Token.Length > 0;

        static KmhSteamDiscoveryClient() => KmhIdentityTeardown.Register("steam discovery", Reset);

        public static void Reset()
        {
            State = SteamDiscoveryState.Idle;
            ErrorCode = ""; LastMatched = LastAdded = 0;
            LastRunUtc = default;
            _ranThisLaunch = false;
        }

        // No linked account or no Steam is the ordinary case, not a failure, so it silently does nothing.
        public static void RunOnceIfOffered()
        {
            if (_ranThisLaunch || !Offered) return;
            _ranThisLaunch = true;
            Run();
        }

        public static void Run()
        {
            if (!_gate.TryEnter()) return;
            State = SteamDiscoveryState.Running;
            Task.Run(async () =>
            {
                try { await RunAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Steam discovery: failed ({ex.GetType().Name}).");
                    State = SteamDiscoveryState.Unavailable;
                }
                finally { _gate.Exit(); }
            });
        }

        private static async Task RunAsync(CancellationToken ct)
        {
            string token = Token;
            if (token.Length == 0) { State = SteamDiscoveryState.Idle; return; }

            // An empty list is sent: it tells Global the stored evidence no longer holds. Only a failed read stops here.
            List<ulong> ids;
            if (!KmhSteamFriends.TryRead(out ids)) { State = SteamDiscoveryState.NoSteam; return; }

            var arr = new JArray();
            foreach (ulong id in ids) arr.Add(id.ToString());

            GlobalHttp.Result r = await GlobalHttp.PostAsync("/api/v1/friends/steam",
                new JObject { ["steam_ids"] = arr }.ToString(Newtonsoft.Json.Formatting.None), token, ct)
                .ConfigureAwait(false);

            if (!r.Ok || r.Body == null)
            {
                ErrorCode = r.ErrorCode;
                // Refused means the player turned discovery off; the page says so instead of offering a futile retry.
                State = r.ErrorCode == "KMH-FRD-756" ? SteamDiscoveryState.Refused
                      : r.Status >= 400 && r.Status < 500 ? SteamDiscoveryState.Rejected : SteamDiscoveryState.Unavailable;
                return;
            }

            LastMatched = (int?)r.Body["matched"] ?? 0;
            // "pending" is always empty: Global keeps it only for older clients.
            LastAdded   = (r.Body["added"]   as JArray)?.Count ?? 0;
            LastRunUtc  = DateTime.UtcNow;
            ErrorCode = "";
            State = SteamDiscoveryState.Done;

            // The list Global holds has changed, so the panel must not keep drawing the one from before it ran.
            if (LastAdded > 0) KmhFriendsClient.Refresh();
        }

        // Lives here so the dialog has no logic of its own to drift out of step.
        public static string Summary()
        {
            switch (State)
            {
                case SteamDiscoveryState.Running:
                    return KmhText.Get("KMH_SteamFind_Running", "Checking your Steam friends…");
                case SteamDiscoveryState.Refused:
                    return KmhText.Get("KMH_SteamFind_Off",
                        "Steam friend matching is off in your privacy settings.");
                case SteamDiscoveryState.Unavailable:
                    return KmhText.Get("KMH_SteamFind_Failed", "Couldn't reach Colony Nexus Global to check Steam friends.");
                case SteamDiscoveryState.NoSteam:
                    return KmhText.Get("KMH_SteamFind_NoSteam", "Steam did not give your friends list, so none were checked.");
                case SteamDiscoveryState.Rejected:
                    return KmhText.Get("KMH_SteamFind_Rejected", "Colony Nexus Global could not check Steam friends ({0}).", ErrorCode);
                case SteamDiscoveryState.Done when LastAdded > 0:
                    return KmhText.Get("KMH_SteamFind_Found", "Steam friends: {0} added as friends.", LastAdded);
                case SteamDiscoveryState.Done:
                    return KmhText.Get("KMH_SteamFind_None", "No new Steam matches.");
                default:
                    return "";
            }
        }

        public static string Explain()
            => KmhText.Get("KMH_SteamFind_Tip",
                "Only mutual matches are added, straight away and without a request: you are friends on Steam, both of "
              + "you have Steam matching on, and they have run Find Steam friends too. Nobody is asked or told when "
              + "there is no match.");

        internal static void SetResultForTest(SteamDiscoveryState state, int added)
        {
            State = state;
            LastAdded = added;
        }
    }
}
