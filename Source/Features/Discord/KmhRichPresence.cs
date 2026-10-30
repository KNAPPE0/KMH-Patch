using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Global;
using KMHPatch.SubProtocol;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Discord
{
    internal static class KmhRichPresence
    {
        internal static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(2);
        // Discord drops updates past five in twenty seconds; only an escalation to Critical may come this soon.
        internal static readonly TimeSpan UrgentFloor = TimeSpan.FromSeconds(5);
        internal static readonly int[] UpdateChoices = { 15, 30, 60, 120 };
        // Quick retries first (Discord often starts just after RimWorld), then back off rather than retry every frame.
        internal static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(3);
        internal static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(60);
        private const int QuickRetries = 6;

        private static int _retries;

        internal static TimeSpan RetryDelay(int attempt) => attempt < QuickRetries ? FirstRetry : RetryEvery;

        private static object _client;
        private static string _appId = "";
        private static DateTime _nextPush = DateTime.MinValue;
        private static DateTime _nextRetry = DateTime.MinValue;
        private static DateTime _lastPushUtc = DateTime.MinValue;
        private static string _lastPayload = "";
        private static bool _loggedConnection;
        private static bool _urgent;
        private static int _epoch;
        private static int _seed;
        private static string _phaseKey = "";
        private static DateTime _phaseStartUtc;
        private static readonly KmhPresenceResolver _resolver = new KmhPresenceResolver();
        private static readonly List<PresenceCandidate> _candidates = new List<PresenceCandidate>();

        internal static int PushCount { get; private set; }
        internal static KmhDiscordRpc.Presence LastSent { get; private set; }
        internal static PresenceKind LastSentKind { get; private set; }
        internal static PresenceChoice LastChoice => _resolver.Current;

        // Bumped whenever the presence is replaced or dropped, so a join delivered against an old one is ignored.
        internal static int Epoch => _epoch;

        internal static string LastState { get; private set; } = "off";

        // False means no Join is advertised: without a URI scheme the library rejects the whole presence.
        internal static bool JoinAdvertisable { get; private set; }

        internal static int RestartsForTest { get; private set; }
        internal static bool SuppressClientForTest;
        internal static DateTime NextRetryForTest => _nextRetry;

        public static bool Active => _client != null;

        public static void Tick()
        {
            if (_shuttingDown) return;
            try
            {
                KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
                string wanted = s.DiscordPresenceEnabled ? (s.DiscordAppId ?? "").Trim() : "";

                // A disposed client is as dead as no client, and only this notices - it stays non-null.
                if (_client != null && !KmhDiscordRpc.IsAlive(_client))
                {
                    KmhLog.Debug("Discord: the RPC client was disposed under us - restarting.");
                    Stop();
                }

                if (wanted != _appId) Restart(wanted);
                else if (_client == null && wanted.Length > 0 && DateTime.UtcNow >= _nextRetry) Restart(wanted);
                if (_client == null) return;

                KmhDiscordRpc.Invoke(_client);   // pumps the library's callbacks, including OnJoin
                KmhDiscordRpc.LogDiagnostics();

                DateTime now = DateTime.UtcNow;
                if (now < _nextPush) return;
                _nextPush = now + SampleEvery;
                Push(now);
            }
            catch (Exception ex) { KmhLog.Debug($"Discord: presence tick failed ({ex.GetType().Name})."); }
        }

        internal static TimeSpan MinInterval(KMHPatchSettings s)
        {
            int seconds = s?.DiscordUpdateSeconds ?? UpdateChoices[0];
            return TimeSpan.FromSeconds(Math.Max(UpdateChoices[0], Math.Min(UpdateChoices[UpdateChoices.Length - 1], seconds)));
        }

        internal static bool ShouldPush(bool changed, bool urgent, DateTime lastPushUtc, DateTime nowUtc, TimeSpan minInterval)
        {
            if (!changed) return false;
            if (lastPushUtc == DateTime.MinValue) return true;
            return nowUtc - lastPushUtc >= (urgent ? UrgentFloor : minInterval);
        }

        private static void Restart(string appId)
        {
            bool sameApp = appId == _appId;
            Stop();
            _appId = appId;
            // A new application id gets the quick attempts again instead of inheriting the old id's backoff.
            if (!sameApp) _retries = 0;
            _nextRetry = DateTime.UtcNow.Add(RetryDelay(_retries));
            _retries++;
            RestartsForTest++;

            if (_appId.Length == 0) { LastState = "no application id set"; return; }
            if (!LooksLikeApplicationId(_appId))
            {
                // Not retried: a malformed id cannot work, and editing it restarts this anyway.
                LastState = "that is not a Discord application id";
                _nextRetry = DateTime.MaxValue;
                return;
            }
            if (SuppressClientForTest) { LastState = "suppressed for tests"; return; }
            if (!KmhDiscordRpc.Available)
            {
                // A library that is not there will not turn up later, so this one is not retried at all.
                LastState = KmhDiscordRpc.Unavailable;
                _nextRetry = DateTime.MaxValue;
                return;
            }

            _client = KmhDiscordRpc.CreateClient(_appId, KMHPatchMod.SettingsOrDefault.AllowDiscordJoin);
            if (_client == null) { LastState = "Discord is not running"; return; }

            // Connected, so the next outage starts its own quick attempts rather than a minute of silence.
            _retries = 0;
            JoinAdvertisable = KmhDiscordRpc.HasUriScheme(_client);

            _epoch++;
            _seed = new System.Random().Next();
            _nextPush = DateTime.MinValue;
            _lastPushUtc = DateTime.MinValue;
            _resolver.Reset();
            KmhDiscordRpc.SubscribeJoin(_client, KmhPresenceJoin.Accept);
            KmhDiscordRpc.SubscribeDiagnostics(_client);
            // Two applications both setting a status gives the player two activities on their profile.
            KmhRwtPresence.Suspend();
            LastState = "connected";
            KmhLog.Debug($"Discord: rich presence started for application {_appId}.");
        }

        // Frames still run while the game quits; without this the next tick starts a fresh client on the way out.
        private static bool _shuttingDown;
        internal static bool ShuttingDown => _shuttingDown;

        public static void Shutdown()
        {
            _shuttingDown = true;
            Stop();
        }

        public static void Stop()
        {
            if (_client != null)
            {
                KmhDiscordRpc.ClearPresence(_client);
                KmhDiscordRpc.Dispose(_client);
                _epoch++;
                KmhLog.Debug("Discord: status cleared and the client closed.");
            }
            _client = null;
            _lastPayload = "";
            _loggedConnection = false;
            _urgent = false;
            _resolver.Reset();
            JoinAdvertisable = false;
            LastState = "off";
            KmhRwtPresence.Resume();
        }

        // A Discord snowflake: digits only, and in this range for every application Discord has ever issued.
        internal static bool LooksLikeApplicationId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 17 || id.Length > 20) return false;
            foreach (char c in id) if (c < '0' || c > '9') return false;
            return true;
        }

        // Only the timer: clearing _lastPayload would resend an unchanged status on every directory refresh and burn
        // Discord's rate limit. Swallows everything: an optional status must never fail admission.
        public static void Invalidate()
        {
            try { _nextPush = DateTime.MinValue; } catch { }
        }

        // A server joined or left, or a setting changed: worth the short floor rather than the whole interval.
        public static void InvalidateNow()
        {
            try { _urgent = true; _nextPush = DateTime.MinValue; } catch { }
        }

        private static void Push(DateTime now)
        {
            KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
            PresenceSession session = Session(s, now);
            KmhPresenceSignals.Sample(_candidates, now, s.DiscordShowGameplay && s.DiscordDetailedActivity);
            PresenceChoice choice = _resolver.Resolve(_candidates, now);
            if (choice.Urgent) _urgent = true;
            KmhDiscordRpc.Presence p = KmhPresenceFormat.Compose(choice, session, s, _seed, now);

            // Discord rate-limits presence updates, and nothing here changes most samples.
            string payload = Payload(p);
            if (!ShouldPush(payload != _lastPayload, _urgent, _lastPushUtc, now, MinInterval(s))) return;
            _lastPayload = payload;
            _lastPushUtc = now;
            _urgent = false;
            PushCount++;
            LastSent = p;
            LastSentKind = choice.Kind;

            object built = KmhDiscordRpc.Build(p);
            if (built != null) KmhDiscordRpc.SetPresence(_client, built);

            // Once per client, because "which Discord is this going to" is the whole answer when two are open.
            if (!_loggedConnection)
            {
                _loggedConnection = true;
                KmhLog.Info($"Discord: status is going to {KmhDiscordRpc.ConnectionInfo(_client)}.");
            }

            // Invite fields logged per push: a missing join secret or party is invisible from the status itself.
            if (KmhLog.DebugEnabled)
                KmhLog.Debug($"Discord: pushed #{PushCount} kind={choice.Kind}/{choice.Tier} details='{p.Details}' state='{p.State}' "
                           + $"party='{p.PartyId}' {p.PartySize}/{p.PartyMax} public={p.PartyPublic} "
                           + $"join={(string.IsNullOrEmpty(p.JoinSecret) ? "NONE" : "set")} "
                           + $"scheme={JoinAdvertisable} large='{p.LargeImageKey}' small='{p.SmallImageKey}' "
                           + $"built={built != null}");
        }

        internal static string Payload(KmhDiscordRpc.Presence p)
            => p == null ? "" : string.Join("\n", new[]
               {
                   p.Details, p.State, p.PartyId, p.PartySize + "/" + p.PartyMax, p.JoinSecret, p.PartyPublic.ToString(),
                   p.LargeImageKey, p.LargeImageText, p.SmallImageKey, p.SmallImageText,
                   p.StartedUtc.HasValue ? p.StartedUtc.Value.Ticks.ToString() : "",
               });

        // Spelled exactly as uploaded under Rich Presence > Art Assets; an unknown key renders no image at all.
        internal const string LargeAsset = "kmh_logo_v1";
        internal const string OfficialAsset = "kmh_official_server_v1";

        // Not localized: it is the product name, and Discord prints its own app name above the status anyway.
        internal const string AppName = "KMH: Colony Nexus";

        // Rich presence authenticates nothing: it hands the local Discord an app id over a pipe. The bot token and
        // client secret belong to the server-side bot and must never ship in the mod; a check asserts this.
        internal const bool NeedsDiscordSecret = false;

        // Main thread only.
        internal static PresenceSession Session(KMHPatchSettings s, DateTime now)
        {
            bool playing = Current.ProgramState == ProgramState.Playing && Current.Game != null;
            var ses = new PresenceSession
            {
                Playing = playing,
                Multiplayer = KmhPresenceSignals.Multiplayer,
                KmhServer = KmhDispatcher.IsKmhServer,
                Card = KmhDispatcher.IsKmhServer ? CardForCurrentServer() : null,
                PrivateServerName = KmhDispatcher.ServerName ?? "",
                JoinAdvertisable = JoinAdvertisable,
            };
            if (Mods.KmhJoinPreparation.Active)
            {
                ServerCardDto target = PublicCard(Mods.KmhJoinPreparation.ServerId);
                ses.JoinServerPublic = target != null;
                ses.JoinServerName = target?.Name ?? "";
            }

            // The elapsed clock restarts with each colony or server, and on returning to the menu.
            string key = playing ? "play|" + RuntimeHelpers.GetHashCode(Current.Game) + "|" + KmhDispatcher.ServerId : "menu";
            if (key != _phaseKey) { _phaseKey = key; _phaseStartUtc = now; }
            ses.StartedUtc = _phaseStartUtc;

            if (playing && s.DiscordShowColonyInfo)
            {
                int colonists = 0;
                foreach (Map map in Find.Maps) colonists += map.mapPawns.FreeColonistsCount;
                foreach (RimWorld.Planet.Caravan c in Find.WorldObjects.Caravans)
                    if (c.IsPlayerControlled) colonists += c.PawnsListForReading.Count(pawn => pawn.IsFreeColonist);
                ses.Colonists = colonists;
                ses.Day = GenDate.DaysPassed;
            }
            return ses;
        }

        private static ServerCardDto PublicCard(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return null;
            foreach (ServerCardDto c in KmhDirectoryClient.Cards)
                if (c.FromPublicDirectory && string.Equals(c.ServerId, serverId, StringComparison.Ordinal)) return c;
            return null;
        }

        internal static KmhDiscordRpc.Presence Build()
        {
            KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
            return KmhPresenceFormat.Compose(_resolver.Current, Session(s, DateTime.UtcNow), s, _seed, DateTime.UtcNow);
        }

        internal static string Describe()
        {
            KmhDiscordRpc.Presence p = LastSent;
            PresenceChoice c = _resolver.Current;
            return $"active={Active} state='{LastState}' kind={c.Kind}/{c.Tier} sent={LastSentKind} pushes={PushCount} "
                 + $"details='{p?.Details}' status='{p?.State}' small='{p?.SmallImageKey}' smallText='{p?.SmallImageText}' "
                 + $"party={p?.PartySize}/{p?.PartyMax} join={(string.IsNullOrEmpty(p?.JoinSecret) ? "none" : "set")} "
                 + $"elapsed={(p?.StartedUtc.HasValue == true ? "on" : "off")} {KmhDiscordRpc.DiagnosticsLine()}";
        }

        // Only a public-directory row counts (not a hand-edited cache), and since the id is only the far end's claim
        // the card must also match the address this game dialled.
        internal static ServerCardDto CardForCurrentServer()
            => CardFor(KmhDispatcher.ServerId, DialledForTest ?? Catalog.CatalogPushGuard.CurrentEndpoint());

        internal static string DialledForTest;

        // A card fetched by id after a Direct Connect counts as much as one the browser listed.
        internal static ServerCardDto CardFor(string id, string dialled)
        {
            if (string.IsNullOrEmpty(id)) return null;
            ServerCardDto c = KmhDirectoryClient.CardById(id);
            return c != null && c.FromPublicDirectory && SameEndpoint(c.JoinEndpoint, dialled) ? c : null;
        }

        // Literal only (DNS would run on the frame pump); a mismatch shows a private server, the safe way to be wrong.
        internal static bool SameEndpoint(string listed, string dialled)
        {
            if (!Split(listed, out string lh, out int lp) || !Split(dialled, out string dh, out int dp)) return false;
            if (lp != dp) return false;
            if (string.Equals(lh, dh, StringComparison.OrdinalIgnoreCase)) return true;
            return System.Net.IPAddress.TryParse(lh, out System.Net.IPAddress a)
                && System.Net.IPAddress.TryParse(dh, out System.Net.IPAddress b)
                && Normal(a).Equals(Normal(b));
        }

        private static System.Net.IPAddress Normal(System.Net.IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

        private static bool Split(string endpoint, out string host, out int port)
        {
            host = ""; port = 0;
            if (string.IsNullOrWhiteSpace(endpoint)) return false;
            string e = endpoint.Trim();
            int colon = e.LastIndexOf(':');
            if (colon <= 0 || colon == e.Length - 1) return false;
            host = e.Substring(0, colon).Trim('[', ']');
            return host.Length > 0
                && int.TryParse(e.Substring(colon + 1), System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out port)
                && port > 0 && port <= 65535;
        }

        internal static void SetJoinAdvertisableForTest(bool value) => JoinAdvertisable = value;
        internal static void SetShuttingDownForTest(bool value) => _shuttingDown = value;

        internal static void ResetForTest()
        {
            _client = null; _appId = ""; _lastPayload = ""; _epoch = 0;
            _nextPush = DateTime.MinValue; _nextRetry = DateTime.MinValue; _lastPushUtc = DateTime.MinValue;
            LastState = "off"; RestartsForTest = 0; PushCount = 0; LastSent = null; LastSentKind = PresenceKind.None; _urgent = false;
            JoinAdvertisable = false; _retries = 0; _loggedConnection = false;
            _phaseKey = ""; _resolver.Reset();
        }
    }
}
