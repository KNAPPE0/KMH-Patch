using System;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;

using KMHPatch.Features.Global;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KMHPatch.Features.Account
{
    internal enum DiscordLinkState { Unknown, Loading, Ready, Linking, Unavailable }

    // Never sees a code, token or secret, and cannot state its own Discord id; Global decides everything.
    internal static class KmhDiscordLinkClient
    {
        // Only while the player is at Discord; ordinary refreshes are on demand.
        private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(3);

        // Past this the state Global minted is dead, so polling on cannot change the answer.
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(6);

        public static DiscordLinkState State { get; private set; } = DiscordLinkState.Unknown;
        public static bool   Linked      { get; private set; }
        // False means no OAuth app on Global, so no button; it must not be reported as Global being unreachable.
        public static bool   Available   { get; private set; }
        public static string DisplayName { get; private set; } = "";
        public static string ErrorCode   { get; private set; } = "";

        // Only the slot: this client's retry timing is its own, not the shared interval backoff.
        private static readonly Global.KmhPollGate _gate = Global.KmhPollGate.SlotOnly();
        public static bool Busy => _gate.Busy;

        private static DateTime _nextPollUtc = DateTime.MinValue;
        private static DateTime _linkingUntilUtc = DateTime.MinValue;

        private static string Directory => GlobalHttp.BaseUrl;

        static KmhDiscordLinkClient() => KmhIdentityTeardown.Register("discord link", Reset);

        public static void Reset()
        {
            State = DiscordLinkState.Unknown;
            Linked = false; Available = false; DisplayName = ""; ErrorCode = "";
            _linkingUntilUtc = DateTime.MinValue;
            // _nextRetryUtc deliberately survives: the no-token path calls Reset, so clearing it restores a per-frame loop.
        }

        // Retrying states loop back on themselves and this runs every frame, so the floor stops a request per frame.
        private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(5);
        private static DateTime _nextRetryUtc = DateTime.MinValue;

        public static void RefreshIfNeeded()
        {
            if (State == DiscordLinkState.Unknown || State == DiscordLinkState.Unavailable)
            {
                if (DateTime.UtcNow < _nextRetryUtc) return;
                _nextRetryUtc = DateTime.UtcNow + RetryEvery;
                Refresh();
                return;
            }

            // The link completes in a browser, so nothing tells the game about it.
            if (State != DiscordLinkState.Linking) return;
            if (DateTime.UtcNow > _linkingUntilUtc) { State = DiscordLinkState.Ready; return; }
            if (DateTime.UtcNow < _nextPollUtc) return;
            _nextPollUtc = DateTime.UtcNow + PollEvery;
            Refresh(keepLinking: true);
        }

        public static void Refresh() => Refresh(false);

        private static void Refresh(bool keepLinking)
        {
            if (!_gate.TryEnter()) return;
            if (!keepLinking) State = DiscordLinkState.Loading;
            Task.Run(async () =>
            {
                try { await StatusAsync(keepLinking, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Discord link: status failed: {ex.Message}");
                    State = DiscordLinkState.Unavailable;
                }
                finally { _gate.Exit(); }
            });
        }

        private static async Task StatusAsync(bool keepLinking, CancellationToken ct)
        {
            string token = KmhAccountStore.TokenFor(Directory);
            if (token.Length == 0) { Reset(); return; }

            GlobalHttp.Result r = await GlobalHttp.GetAsync("/api/v1/player/discord", token, ct).ConfigureAwait(false);
            if (!r.Ok || r.Body == null)
            {
                // A failed poll mid-flow is not a failed link - the browser half is still running.
                if (keepLinking) return;
                State = DiscordLinkState.Unavailable; ErrorCode = r.ErrorCode;
                return;
            }
            Apply(r.Body);
        }

        // The URL is Global's: application id, scope and state are all decided there.
        public static void BeginLink()
        {
            if (!_gate.TryEnter()) return;
            ErrorCode = "";
            Task.Run(async () =>
            {
                try { await BeginAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Discord link: start failed: {ex.Message}");
                    State = DiscordLinkState.Unavailable;
                }
                finally { _gate.Exit(); }
            });
        }

        private static async Task BeginAsync(CancellationToken ct)
        {
            string token = KmhAccountStore.TokenFor(Directory);
            if (token.Length == 0) { Reset(); return; }

            GlobalHttp.Result r = await GlobalHttp.PostAsync("/api/v1/player/discord/link", "{}", token, ct)
                                                  .ConfigureAwait(false);
            if (!r.Ok || r.Body == null)
            {
                State = DiscordLinkState.Unavailable; ErrorCode = r.ErrorCode;
                return;
            }

            string url = (string)r.Body["authorize_url"] ?? "";
            if (!IsDiscordAuthorizeUrl(url)) { State = DiscordLinkState.Unavailable; return; }

            State = DiscordLinkState.Linking;
            _linkingUntilUtc = DateTime.UtcNow + GiveUpAfter;
            _nextPollUtc = DateTime.UtcNow + PollEvery;

            // Opening a URL is a main-thread action, and this ran on a pool thread.
            KmhMainThread.Post(() =>
            {
                try { Application.OpenURL(url); }
                catch (Exception ex) { KmhLog.Debug($"Discord link: could not open a browser: {ex.Message}"); }
            });
        }

        // A URL from the network, handed to the shell. A substituted directory could otherwise launch anything.
        internal static bool IsDiscordAuthorizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;

            string host = uri.Host;
            return host == "discord.com" || host.EndsWith(".discord.com", StringComparison.Ordinal);
        }

        public static void Unlink()
        {
            if (!_gate.TryEnter()) return;
            Task.Run(async () =>
            {
                try
                {
                    string token = KmhAccountStore.TokenFor(Directory);
                    if (token.Length == 0) { Reset(); return; }

                    GlobalHttp.Result r = await GlobalHttp
                        .PostAsync("/api/v1/player/discord/unlink", "{}", token, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!r.Ok || r.Body == null) { State = DiscordLinkState.Unavailable; ErrorCode = r.ErrorCode; return; }
                    Apply(r.Body);
                }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Discord link: unlink failed: {ex.Message}");
                    State = DiscordLinkState.Unavailable;
                }
                finally { _gate.Exit(); }
            });
        }

        // Read back from what Global returned, never from what was asked for.
        private static void Apply(JObject body)
        {
            Linked = (bool?)body["linked"] ?? false;
            // Absent means an older Global without the field; it still has the endpoints, so the button stays.
            Available = (bool?)body["available"] ?? true;
            DisplayName = Sanitize((string)body["display_name"] ?? "");
            ErrorCode = "";
            State = DiscordLinkState.Ready;
            if (Linked) _linkingUntilUtc = DateTime.MinValue;
        }

        // Widgets.Label parses rich text, and an older Global may not have stripped it.
        internal static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsControl(c) || c == '<' || c == '>') continue;
                sb.Append(c);
                if (sb.Length >= 48) break;
            }
            return sb.ToString().Trim();
        }
    }
}
