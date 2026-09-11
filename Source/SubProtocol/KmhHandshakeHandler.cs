using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Notifications;

namespace KMHPatch.SubProtocol
{
    internal static class KmhHandshakeHandler
    {
        // Volatile: Send runs on whichever thread feature code is on, while OnPong fires from the chat-receive thread.
        private static volatile object _lastPingSentBox;

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.Hello,  OnHello);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.Pong,   OnPong);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.Notice, OnNotice);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.OpResult, OnOpResult);
        }

        private static void OnOpResult(KmhEnvelope env) => KmhOpId.SettledById(env?.GetString("op"));

        public static bool SendPing()
        {
            _lastPingSentBox = DateTime.UtcNow;
            return KmhDispatcher.Send(KmhProtocol.Kind.Ping, null);
        }

        // Both transports deliver a hello, so an older revision must not put stale presentation over fresh.
        private static int _commsRev;

        internal static void ResetCommsRevision() => _commsRev = 0;

        // A server counts from 1 again on restart, so a number carried over from an ended session means nothing.
        internal static int AppliedRevisionFor(bool inSession, int applied) => inSession ? applied : 0;

        // 0 means the server never said, and equal is a legitimate re-send of the same generation.
        internal static bool IsStaleRevision(int incoming, int applied) => incoming > 0 && incoming < applied;

        // Nothing here affects permission, identity or what the session may do, so skipping a stale one costs only colours.
        private static void ApplyPresentation(KmhEnvelope env)
        {
            UI.KmhTheme.ApplyServerTheme(env.GetString("theme_accent"),  env.GetString("theme_server"),
                                         env.GetString("theme_guild"),  env.GetString("theme_dm"),
                                         env.GetString("theme_discord"));
            UI.KmhTheme.ApplyServerChatTheme(env.GetString("theme_name"),    env.GetString("theme_text"),
                                             env.GetString("theme_name_dc"), env.GetString("theme_text_dc"),
                                             env.GetString("discord_marker"));
            // Absent on an older server, which reads as "no preference" and leaves KMH's own marker colours standing.
            Features.Sites.KmhMarkerColors.ApplyServerColors(env.GetString("marker_mine"), env.GetString("marker_theirs"));
            Features.Identity.KmhStaff.ApplyWire(env.GetString("staff_badges"));
            // Who holds each role, not just how a role looks: without it a badge never appears for an offline author.
            Features.Identity.KmhStaff.ApplyRoles(env.GetString("staff_roles"));
            Features.Chat.ChatImageCache.SetServerMaxBytes(env.GetInt("max_image_bytes", 0));   // 0/absent -> KMH default
            // Absent on an older server, which reads as "no resolver" and leaves the plain url path alone.
            Features.Chat.ChatMediaClient.SetAvailable(env.GetBool("media_resolver"));
            Features.Chat.ChatVideoPlayer.SetMaxSeconds(env.GetInt("max_video_seconds", 0));
            Features.Chat.ChatYouTube.Configure(env.GetBool("yt_playback"), env.GetInt("yt_max_height", 720),
                                                env.GetInt("yt_max_seconds", 0));
            Features.Chat.ChatVideoServer.Configure(env.GetBool("yt_server"));

            KmhLog.Info($"KMH presentation applied: {Features.Identity.KmhStaff.RoleCount} staff role(s), "
                      + $"{Features.Identity.KmhStaff.BadgeCount} badge style(s), "
                      + $"media resolver {(Features.Chat.ChatMediaClient.Available ? "on" : "off")}, "
                      + $"video links {(Features.Chat.ChatYouTube.Available ? "playable in game" : "browser only")}");
        }

        private static void OnHello(KmhEnvelope env)
        {
            Features.KmhFeatures.SetDisabled(env.GetString("disabled"));   // null when omitted -> keep last-known-good
            string caps = env.GetString("capabilities");
            KmhCapabilities.Apply(caps, present: caps != null);            // absent -> keep the inferred v1.2.1 baseline
            KmhDispatcher.ServerName = env.GetString("server_name") ?? "";   // shown so players can tell servers apart
            // A REQUEST only. It never starts a transmission - the pump raises a consent prompt and the player decides.
            Diagnostics.KmhDebugUplink.ServerRequested = env.GetBool("debug_uplink");

            // Presentation only: returning from the whole handler would skip the ack and the dial, so the session never starts.
            int rev = env.GetInt("comms_rev", 0);
            _commsRev = AppliedRevisionFor(KmhDispatcher.IsKmhServer, _commsRev);
            bool stale = IsStaleRevision(rev, _commsRev);
            if (stale) KmhLog.Debug($"KMH hello: keeping Communications revision {_commsRev}, this hello carries {rev}");
            else
            {
                if (rev > 0) _commsRev = rev;
                ApplyPresentation(env);
            }

            if (!ActivateSession(env.GetInt("v", 0), env.GetString("build") ?? "", KmhTransportStatus.ChatFallback))
                return;

            // The one-time token arrives here and nowhere earlier, so this is the only place the API can be dialled.
            bool apiComing = KMHPatchMod.Settings?.UseKmhApiTransport == true && env.GetBool("api_enabled");
            try
            {
                if (apiComing)
                {
                    // If the owner disabled chat fallback the client must not tunnel over chat, whatever its own setting says.
                    bool effFallback = KMHPatchMod.Settings.AllowChatTransportFallback && env.GetBool("allow_chat_fallback", true);
                    if (!KmhApiClient.Active)
                    {
                        // Host preference: player's local override > server-advertised public host > RWT IP we dialed.
                        string advertised = env.GetString("api_host") ?? "";
                        string host = !string.IsNullOrEmpty(KMHPatchMod.Settings.KmhApiHostOverride) ? KMHPatchMod.Settings.KmhApiHostOverride
                                    : !string.IsNullOrEmpty(advertised) ? advertised
                                    : TCPNetwork.Network.Ip;
                        int port = env.GetInt("api_port", KMHPatchMod.Settings.KmhApiPort);
                        if (port <= 0) port = KMHPatchMod.Settings.KmhApiPort;
                        KmhApiClient.Connect(host, port, SessionHandler.Username, env.GetString("api_token") ?? "", effFallback);
                    }
                    else KmhApiClient.SetChatFallbackAllowed(effFallback);   // link already up: apply the server's policy
                }
            }
            catch (Exception ex) { KmhLog.Warn($"KMH API: connect attempt threw: {ex.Message}"); apiComing = false; }

            // Chat-only session: when the API is coming up, its own activation hydrates instead.
            if (!apiComing) TryHydrateSession();
        }

        // Shared by the chat hello and the API ack, so it is idempotent and never downgrades a live API link to chat.
        internal static bool ActivateSession(int serverVersion, string serverBuild, KmhTransportStatus status)
        {
            if (serverVersion != KmhProtocol.CurrentVersion)
            {
                bool wasMismatch = KmhTransport.Status == KmhTransportStatus.VersionMismatch;   // toast once, not every re-handshake
                KmhLog.Warn($"Server KMH version {serverVersion} does not match client {KmhProtocol.CurrentVersion} - disabling KMH features for this session");
                KmhDispatcher.IsKmhServer = false;
                UI.KmhDashboardState.ClearConfirmed();   // real mismatch: don't let the sticky flag mask it
                KmhTransport.Status = KmhTransportStatus.VersionMismatch;
                if (!wasMismatch)
                    KmhNotifications.Rejected($"KMH disabled: server protocol v{serverVersion} vs your v{KmhProtocol.CurrentVersion}. Update the KMH-Patch mod to match this server.");
                return false;
            }

            bool first = !KmhDispatcher.IsKmhServer;
            KmhDispatcher.IsKmhServer = true;
            if (first) { ResetHydrationForTest(); Diagnostics.KmhSelfTest.RunOnceIfDebug(); }   // fresh session: hydrate again from scratch
            UI.KmhDashboardState.MarkConfirmed();   // sticky: keep feature buttons visible across a re-handshake
            KmhDispatcher.ServerProtocolVersion = serverVersion;
            KmhDispatcher.ServerBuild = serverBuild ?? "";
            // Before any hydration below, or the router's handshake gate drops every label and snapshot request.
            KmhDispatcher.Send(KmhProtocol.Kind.HelloAck, new { v = KmhProtocol.CurrentVersion });
            if (status == KmhTransportStatus.ApiConnected || KmhTransport.Status != KmhTransportStatus.ApiConnected)
                KmhTransport.Status = status;   // don't downgrade a live API link to chat

            if (status == KmhTransportStatus.ApiConnected) TryHydrateSession();

            if (!first) return true;

            string buildLabel = string.IsNullOrEmpty(KmhDispatcher.ServerBuild) ? "<pre-1.1.0>" : KmhDispatcher.ServerBuild;
            KmhLog.Info($"KMH server detected (protocol v{serverVersion}, build '{buildLabel}', via {(status == KmhTransportStatus.ApiConnected ? "API" : "chat")}) - features enabled");
            KmhNotifications.Positive(string.IsNullOrEmpty(KmhDispatcher.ServerName)
                ? $"KMH server connected (protocol v{serverVersion})"
                : $"Connected to KMH server '{KmhDispatcher.ServerName}' (protocol v{serverVersion})");

            // A server that sent a manifest is trusted to self-describe, so only one too old to send any is nagged.
            if (!KmhCapabilities.ManifestSeen)
            {
                if (string.IsNullOrEmpty(KmhDispatcher.ServerBuild))
                    KmhNotifications.Neutral("This server runs an older KMH build (pre-1.1.0). New features (auctions, want board, world events) stay hidden until the server owner updates.");
                else
                    KmhNotifications.Neutral($"This server runs an older KMH build ({KmhDispatcher.ServerBuild}). Newer KMH features stay hidden until the owner updates the addon; everything it does support works normally.");
            }

            string endpoint = string.IsNullOrEmpty(TCPNetwork.Network.Ip) ? "" : $"{TCPNetwork.Network.Ip}:{TCPNetwork.Network.Port}";
            Extensibility.KmhClientEventBus.Instance.RaiseKmhServerConnected(
                new KMH.Sdk.Client.Events.KmhServerConnectedEvent { ServerProtocolVersion = serverVersion, Endpoint = endpoint });

            // Remember this KMH server + its versions for the local server directory (RWT version = ours, matched at login).
            try
            {
                Features.Servers.SeenServersStore.Record(endpoint, KmhDispatcher.ServerBuild, RwtCompat.ExecutableVersion);
            }
            catch (Exception ex) { KmhLog.Warn($"Seen-servers record threw: {ex.Message}"); }

            return true;
        }

        // Deferred to whichever transport actually carries it, or ~20 packets land on the chat carrier before the API connects.
        private static bool _catalogPushed;
        private static bool _snapshotsRequested;

        // Hydration counts as done only when this is empty, so a send refused at startup cannot leave the map blank all session.
        private static readonly List<string> _pendingHydration = new List<string>();
        private static int _hydrationAttempts;
        internal const int MaxHydrationAttempts = 5;

        internal static void TryHydrateSession()
        {
            TryPushCatalog();
            if (_snapshotsRequested) { RetryPendingHydration(); return; }
            _snapshotsRequested = true;
            try
            {
                KmhRefresh.RequestAll(out List<string> failed);
                _pendingHydration.Clear();
                _pendingHydration.AddRange(failed);
            }
            catch (Exception ex) { KmhLog.Warn($"KMH join hydration threw: {ex.Message}"); }
        }

        // Bounded and driven by existing traffic, so a server that never accepts these costs a few packets, not a loop.
        private static float _lastRetryReal = -999f;

        internal static void RetryPendingHydration()
        {
            if (!KmhDispatcher.IsKmhServer) return;
            bool catalogPending = !_catalogPushed;
            if (_pendingHydration.Count == 0 && !catalogPending) return;
            if (_hydrationAttempts >= MaxHydrationAttempts) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastRetryReal < 3f && now >= _lastRetryReal) return;
            _lastRetryReal = now;
            _hydrationAttempts++;

            // The catalog decides what the server can price and classify, so a failed push cannot wait for the next reconnect.
            if (catalogPending) TryPushCatalog();

            foreach ((string name, Func<bool> send) in KmhRefresh.All)
            {
                if (!_pendingHydration.Contains(name)) continue;
                bool ok = false;
                try { ok = send(); } catch { }
                if (ok) _pendingHydration.Remove(name);
            }
            if (_pendingHydration.Count == 0 && _catalogPushed) KmhLog.Debug("KMH hydration: complete.");
        }

        // A reconnect is a different world and possibly a different server, so hydration starts over.
        internal static void ResetHydrationForTest()
        {
            _catalogPushed = false; _snapshotsRequested = false;
            _pendingHydration.Clear(); _hydrationAttempts = 0;
            Features.Catalog.SiteMetadataSender.ResetForServerSwitch();
        }

        internal static bool HydrationComplete => HydrationCompleteWhen(_snapshotsRequested, _pendingHydration.Count);

        // The rule alone, so a test can prove a pending request blocks completion without a live transport.
        internal static bool HydrationCompleteWhen(bool requested, int pending) => requested && pending == 0;

        private static void TryPushCatalog()
        {
            if (!_catalogPushed)
            {
                bool ok = false;
                try { ok = Features.Catalog.ItemLabelsSender.PushOnce(); }
                catch (Exception ex) { KmhLog.Warn($"ItemLabels: catalog push threw: {ex.Message}"); }
                if (ok) _catalogPushed = true;
            }

            // Its own latch, cleared by the server naming a different catalog: behind the label latch a refused metadata push was never retried.
            if (!Features.Catalog.SiteMetadataSender.NeedsPush) return;
            try { Features.Catalog.SiteMetadataSender.PushOnce(); }
            catch (Exception ex) { KmhLog.Warn($"SiteMeta: metadata push threw: {ex.Message}"); }
        }

        private static void OnNotice(KmhEnvelope env)
        {
            string text = env?.GetString("text");
            if (string.IsNullOrEmpty(text)) return;
            switch (env.GetString("level") ?? "neutral")
            {
                case "positive": KmhNotifications.Positive(text); break;
                case "negative": KmhNotifications.Negative(text); break;
                default:         KmhNotifications.Neutral(text);  break;
            }
        }

        private static void OnPong(KmhEnvelope env)
        {
            object sentBox = _lastPingSentBox;
            if (sentBox is DateTime sent)
            {
                double ms = (DateTime.UtcNow - sent).TotalMilliseconds;
                KmhLog.Debug($"Pong received from server (round-trip {ms:F1} ms)");
            }
            else
            {
                // unsolicited pong (server-initiated heartbeat or stale reply)
                KmhLog.Debug("Pong received from server (unsolicited)");
            }
        }
    }
}
