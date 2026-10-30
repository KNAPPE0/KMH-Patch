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
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.Incompatible, OnIncompatible);
        }

        // The server reached the same verdict first, and may be about to disconnect us - say why before it does.
        private static void OnIncompatible(KmhEnvelope env)
        {
            int serverVersion = env?.GetInt("server_v", 0) ?? 0;
            if (KmhTransport.Status == KmhTransportStatus.VersionMismatch) return;   // already said once
            KmhDispatcher.IsKmhServer = false;
            UI.KmhDashboardState.ClearConfirmed();
            KmhTransport.Status = KmhTransportStatus.VersionMismatch;
            if (env?.GetString("reason") == GameVersionReason)
            {
                string serverGame = env.GetString("server_game") ?? "";
                KmhLog.Warn($"RimWorld version refused by the server: server RimWorld {serverGame}, this game {Compat.KmhGameVersion.Full}");
                KmhNotifications.Rejected(GameVersionText(serverGame, Compat.KmhGameVersion.Full));
                return;
            }
            KmhLog.Warn($"KMH protocol mismatch reported by the server: server v{serverVersion}, client v{KmhProtocol.CurrentVersion}");
            NotifyMismatch(serverVersion);
        }

        internal const string GameVersionReason = "game_version";

        // Two RimWorld versions are not known to share a world, so KMH stays off; an enforcing server refuses the join.
        internal static bool RefuseGameVersion(string serverGame)
        {
            if (Compat.KmhGameVersion.CompareWithMine(serverGame) != Compat.GameMatch.Differs) return false;
            bool said = KmhTransport.Status == KmhTransportStatus.VersionMismatch;
            KmhLog.Warn($"RimWorld version mismatch: the server runs RimWorld {serverGame}, this game is {Compat.KmhGameVersion.Full} - KMH stays off for this session");
            KmhDispatcher.Send(KmhProtocol.Kind.Incompatible,
                new { client_v = KmhProtocol.CurrentVersion, server_v = KmhProtocol.CurrentVersion, reason = GameVersionReason,
                      client_game = Compat.KmhGameVersion.Full, server_game = serverGame });
            KmhDispatcher.IsKmhServer = false;
            UI.KmhDashboardState.ClearConfirmed();
            KmhTransport.Status = KmhTransportStatus.VersionMismatch;
            if (!said) KmhNotifications.Rejected(GameVersionText(serverGame, Compat.KmhGameVersion.Full));
            return true;
        }

        internal static string GameVersionText(string serverGame, string mine)
            => $"This server runs RimWorld {Compat.KmhGameVersion.MajorMinorOf(serverGame)} and you are running {mine}. Players on "
             + "different RimWorld versions cannot share a server yet, so Colony Nexus stays off for this session. Play here on RimWorld "
             + $"{Compat.KmhGameVersion.MajorMinorOf(serverGame)} (Steam: RimWorld > Properties > Betas). (This is the game version, not Colony Nexus or RimWorld Together.)";

        // Named as a KMH problem, never an RWT one: they are separate compatibility contracts.
        internal static void NotifyMismatch(int serverVersion)
        {
            // Equal versions are no mismatch; claiming one sends a player hunting for an update that does not exist.
            if (serverVersion == KmhProtocol.CurrentVersion || serverVersion <= 0)
            {
                KmhNotifications.Rejected(
                    "The server refused this Colony Nexus session even though both sides speak Colony Nexus Protocol v"
                  + $"{KmhProtocol.CurrentVersion}. Reconnect; if it keeps happening the server's log says why. "
                  + "(This is Colony Nexus, not RimWorld Together.)");
                return;
            }
            KmhNotifications.Rejected(
                $"Colony Nexus version mismatch. This server requires Colony Nexus Protocol v{serverVersion}; your client is using "
              + $"v{KmhProtocol.CurrentVersion}. Update Colony Nexus to match the server. (This is Colony Nexus, not RimWorld Together.)");
        }

        private static void OnOpResult(KmhEnvelope env) => KmhOpId.SettledById(env?.GetString("op"));

        // The hello came from a player using the server's reserved name on a server without Colony Nexus.
        internal static void StockServerDetected()
        {
            bool wasOn = KmhDispatcher.IsKmhServer;
            KmhLog.Warn("Session: this server echoed Colony Nexus traffic back as chat, so it does not run Colony Nexus; "
                      + "messages under the server's name came from a player and are ignored for this connection.");
            // Everything the forged hello set goes with it; the mark survives, so later forgeries stay ignored.
            KmhDispatcher.ResetSession();
            KmhClientCaches.ClearAll();
            Features.Enforcement.EnforcementHandler.ResetConnectionState();
            KmhDispatcher.MarkStockServer();
            UI.KmhDashboardState.ClearConfirmed();
            Features.World.GameComponent_KMHWorldWeather.EndAllFromThisSession();
            if (wasOn)
                KmhNotifications.Rejected("This server does not run Colony Nexus, but someone sent Colony Nexus messages under the server's name. "
                                        + "Colony Nexus is off for this session and ignores them.");
        }

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
            Features.Identity.KmhStaff.SimulatedRanked = env.GetBool("sim_ranked");
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
            // A join rerouted to this PC must have reached the server the player picked, not another one on that port.
            if (!Features.Global.KmhLocalServer.HelloMatches(env.GetString("server_id")))
            {
                KmhLog.Warn($"Join: the server on this PC's port {TCPNetwork.Network.Port} is not the one picked in the browser - leaving it");
                KmhDispatcher.IsKmhServer = false;
                KmhNotifications.Rejected($"A different server is running on this PC's port {TCPNetwork.Network.Port}, so Colony Nexus left it "
                                        + "instead of joining the wrong one. Stop that server, or join it from RimWorld Together directly.");
                KmhMainThread.Post(() => RwtCompat.DisconnectToMainMenu());
                return;
            }

            // First, before Config Enforcement can apply a profile: an older server does not say, so its listing does.
            string serverGame = env.GetString("rimworld");
            if (string.IsNullOrEmpty(serverGame))
                serverGame = Features.Global.KmhDirectoryClient.CardById(env.GetString("server_id") ?? "")?.RimWorldVersion ?? "";
            if (RefuseGameVersion(serverGame)) return;
            if (!Features.Enforcement.EnforcementAdmission.Begin(env)) return;
            Features.KmhFeatures.SetDisabled(env.GetString("disabled"));   // null when omitted -> keep last-known-good
            string caps = env.GetString("capabilities");
            KmhCapabilities.Apply(caps, present: caps != null);            // absent -> keep the inferred baseline
            KmhDispatcher.ServerName = env.GetString("server_name") ?? "";
            // Absent on an older server: history then stays keyed on the endpoint until a connection teaches it this id.
            KmhDispatcher.ServerId = env.GetString("server_id") ?? "";
            // An older server omits it and takes no reports; one without reports_v ignores destinations.
            Features.Reports.KmhReportClient.ApplyHello(env.GetString("reports"), env.GetInt("reports_v", 0));
            // Asked for now that the audience is known. A player with no Global account simply stays unverified.
            Features.Global.KmhJoinTicketClient.PresentAsync(KmhDispatcher.ServerId);
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
                        // A join rerouted to this PC keeps the API local too: the public host has the same loopback problem.
                        string advertised = Features.Global.KmhLocalServer.IsRerouted(TCPNetwork.Network.Ip, TCPNetwork.Network.Port)
                                          ? "" : env.GetString("api_host") ?? "";
                        string host = !string.IsNullOrEmpty(KMHPatchMod.Settings.KmhApiHostOverride) ? KMHPatchMod.Settings.KmhApiHostOverride
                                    : !string.IsNullOrEmpty(advertised) ? advertised
                                    : TCPNetwork.Network.Ip;
                        int port = env.GetInt("api_port", KMHPatchMod.Settings.KmhApiPort);
                        if (port <= 0) port = KMHPatchMod.Settings.KmhApiPort;
                        // Only the server's own advertisement is checked against where RWT was reached; the player's override is theirs.
                        bool advertisedUsed = string.IsNullOrEmpty(KMHPatchMod.Settings.KmhApiHostOverride) && !string.IsNullOrEmpty(advertised);
                        KmhApiClient.Connect(host, port, SessionHandler.Username, env.GetString("api_token") ?? "", effFallback,
                                             advertisedUsed ? TCPNetwork.Network.Ip : null);
                    }
                    else KmhApiClient.SetChatFallbackAllowed(effFallback);
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
                KmhLog.Warn($"KMH protocol mismatch: server v{serverVersion}, client v{KmhProtocol.CurrentVersion} - disabling KMH features for this session");
                // Before the teardown, or Send refuses it; an enforcing server holds PostLogin waiting on this.
                KmhDispatcher.Send(KmhProtocol.Kind.Incompatible,
                    new { client_v = KmhProtocol.CurrentVersion, server_v = serverVersion });
                KmhDispatcher.IsKmhServer = false;
                UI.KmhDashboardState.ClearConfirmed();   // real mismatch: don't let the sticky flag mask it
                KmhTransport.Status = KmhTransportStatus.VersionMismatch;
                if (!wasMismatch) NotifyMismatch(serverVersion);
                return false;
            }

            bool first = !KmhDispatcher.IsKmhServer;
            KmhDispatcher.IsKmhServer = true;
            Features.Discord.KmhRichPresence.InvalidateNow();   // the server is only named from here on
            Features.Friends.KmhPresenceReporter.Invalidate();
            // No self-test here: run synchronously in the handshake it freezes the game until RWT drops the socket.
            if (first) ResetHydrationForTest();
            UI.KmhDashboardState.MarkConfirmed();   // sticky: keep feature buttons visible across a re-handshake
            KmhDispatcher.ServerProtocolVersion = serverVersion;
            KmhDispatcher.ServerBuild = serverBuild ?? "";
            // Before any hydration, or the router's handshake gate drops every label and snapshot request.
            KmhDispatcher.Send(KmhProtocol.Kind.HelloAck,
                new { v = KmhProtocol.CurrentVersion, client_capabilities = KmhClientCapabilities.All, rimworld = Compat.KmhGameVersion.Full });
            if (status == KmhTransportStatus.ApiConnected || KmhTransport.Status != KmhTransportStatus.ApiConnected)
                KmhTransport.Status = status;   // don't downgrade a live API link to chat

            if (status == KmhTransportStatus.ApiConnected) TryHydrateSession();

            if (!first) return true;

            string buildLabel = string.IsNullOrEmpty(KmhDispatcher.ServerBuild) ? "<pre-1.1.0>" : KmhDispatcher.ServerBuild;
            KmhLog.Info($"KMH server detected (protocol v{serverVersion}, build '{buildLabel}', via {(status == KmhTransportStatus.ApiConnected ? "API" : "chat")}) - features enabled");
            KmhMainThread.Post(() => KmhNotifications.Positive(ConnectedToast(serverVersion)));

            // A server that sent a manifest is trusted to self-describe, so only one too old to send any is nagged.
            if (!KmhCapabilities.ManifestSeen)
            {
                if (string.IsNullOrEmpty(KmhDispatcher.ServerBuild))
                    KmhNotifications.Neutral("This server runs an older Colony Nexus build (pre-1.1.0). New features (auctions, want board, world events) stay hidden until the server owner updates.");
                else
                    KmhNotifications.Neutral($"This server runs an older Colony Nexus build ({KmhDispatcher.ServerBuild}). Newer Colony Nexus features stay hidden until the owner updates the addon; everything it does support works normally.");
            }

            // The listed address, not 127.0.0.1, when a server on this PC was dialled locally.
            string endpoint = string.IsNullOrEmpty(TCPNetwork.Network.Ip) ? ""
                : Features.Global.KmhLocalServer.ListedFor(TCPNetwork.Network.Ip, TCPNetwork.Network.Port) ?? $"{TCPNetwork.Network.Ip}:{TCPNetwork.Network.Port}";
            Extensibility.KmhClientEventBus.Instance.RaiseKmhServerConnected(
                new KMH.Sdk.Client.Events.KmhServerConnectedEvent { ServerProtocolVersion = serverVersion, Endpoint = endpoint });

            // Our own RWT version stands in for the server's: RWT refuses a mismatch at login.
            try
            {
                // A Direct Connect never loads the browser's list; one lookup lets the directory vouch for this server.
                if (Features.Global.GlobalHttp.IsFirstParty && !string.IsNullOrEmpty(KmhDispatcher.ServerId))
                    Features.Global.KmhDirectoryClient.EnsureCardsFor(new[] { KmhDispatcher.ServerId });
                string id = Features.Servers.SeenServersStore.TrustedId(KmhDispatcher.ServerId, endpoint,
                    Features.Global.GlobalHttp.IsFirstParty ? Features.Global.KmhDirectoryClient.Cards : null);
                Features.Servers.SeenServersStore.Record(id, KmhDispatcher.ServerName, endpoint,
                                                         KmhDispatcher.ServerBuild, RwtCompat.ExecutableVersion);
            }
            catch (Exception ex) { KmhLog.Warn($"Seen-servers record threw: {ex.Message}"); }

            return true;
        }

        // Named as every other notice names a server, never by its hello alone; main thread, as it reads the directory cards.
        internal static string ConnectedToast(int serverVersion)
            => string.IsNullOrEmpty(KmhDispatcher.ServerName)
                ? $"Colony Nexus server connected (protocol v{serverVersion})"
                : $"Connected to Colony Nexus server '{KmhServerLabel.Current()}' (protocol v{serverVersion})";

        // Deferred to whichever transport actually carries it, or ~20 packets land on the chat carrier before the API connects.
        private static bool _catalogPushed;
        private static bool _snapshotsRequested;

        // Hydration counts as done only when this is empty, so a send refused at startup cannot leave the map blank all session.
        private static readonly List<string> _pendingHydration = new List<string>();
        private static int _hydrationAttempts;
        internal const int MaxHydrationAttempts = 5;

        internal static void TryHydrateSession()
        {
            if (Features.Enforcement.EnforcementAdmission.Pending) return;
            // Timed: this is the main thread during a join, where a stall is otherwise silent.
            var hydrationClock = System.Diagnostics.Stopwatch.StartNew();
            TryPushCatalog();
            long catalogMs = hydrationClock.ElapsedMilliseconds;
            if (_snapshotsRequested) { RetryPendingHydration(); return; }
            _snapshotsRequested = true;
            try
            {
                KmhRefresh.RequestAll(out List<string> failed);
                _pendingHydration.Clear();
                _pendingHydration.AddRange(failed);
                KmhLog.Debug($"KMH join hydration: catalog {catalogMs}ms, snapshots "
                           + $"{hydrationClock.ElapsedMilliseconds - catalogMs}ms, total {hydrationClock.ElapsedMilliseconds}ms.");
            }
            catch (Exception ex) { KmhLog.Warn($"KMH join hydration threw: {ex.Message}"); }
        }

        // Bounded and driven by existing traffic, so a server that never accepts these costs a few packets, not a loop.
        private static float _lastRetryReal = -999f;

        // Admission drops all non-control messages, so retrying then would spend the bounded attempts for nothing.
        internal static bool MayRetryHydration(bool isKmhServer, bool admissionPending)
            => isKmhServer && !admissionPending;

        internal static void RetryPendingHydration()
        {
            if (!MayRetryHydration(KmhDispatcher.IsKmhServer, Features.Enforcement.EnforcementAdmission.Pending)) return;
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

            // Separate latch, reset when the server names another catalog, so a refused metadata push is retried.
            if (!Features.Catalog.SiteMetadataSender.NeedsPush) return;
            try { Features.Catalog.SiteMetadataSender.PushOnce(); }
            catch (Exception ex) { KmhLog.Warn($"SiteMeta: metadata push threw: {ex.Message}"); }
        }

        private static void OnNotice(KmhEnvelope env)
        {
            string text = env?.GetString("text");
            if (string.IsNullOrEmpty(text)) return;
            // Server text can quote what another player typed; no server notice uses markup.
            text = UI.KmhDisplayText.Inert(text, 600);
            switch (env.GetString("level") ?? "neutral")
            {
                case "positive": KmhNotifications.FromServer(text, RimWorld.MessageTypeDefOf.PositiveEvent); break;
                case "negative": KmhNotifications.FromServer(text, RimWorld.MessageTypeDefOf.NegativeEvent); break;
                default:         KmhNotifications.FromServer(text);  break;
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
                KmhLog.Debug("Pong received from server (unsolicited)");
            }
        }
    }
}
