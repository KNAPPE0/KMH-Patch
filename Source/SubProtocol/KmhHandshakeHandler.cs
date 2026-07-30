using System;
using KMHPatch.Diagnostics;
using KMHPatch.Notifications;

namespace KMHPatch.SubProtocol
{
    // Built-in handshake handler. Flips KmhDispatcher.IsKmhServer to true once the server announces compatible KMH
    // support - the gate that allows outbound KMH traffic, so a patched client on a stock RWT server never reaches
    // the .Send() path (fail-safe by design).
    internal static class KmhHandshakeHandler
    {
        // Last time SendPing was called - used by OnPong to compute round-trip. Volatile because Send happens on
        // whichever thread feature code is running on, while OnPong fires from the chat-receive thread
        private static volatile object _lastPingSentBox;

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.Hello,  OnHello);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.Pong,   OnPong);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.Notice, OnNotice);
        }

        // Public API for diagnostic UI (e.g., the in-game KMH tab Ping button). Records the send timestamp so
        // OnPong can report round-trip ms
        public static bool SendPing()
        {
            _lastPingSentBox = DateTime.UtcNow;
            return KmhDispatcher.Send(KmhProtocol.Kind.Ping, null);
        }

        // Chat-transport handshake. Activates the session, acks, and (if the API didn't already come up on connect)
        // dials it using the one-time token the server advertised.
        private static void OnHello(KmhEnvelope env)
        {
            Features.KmhFeatures.SetDisabled(env.GetString("disabled"));   // null when omitted -> keep last-known-good
            KmhDispatcher.ServerName = env.GetString("server_name") ?? "";   // shown so players can tell servers apart
            Diagnostics.KmhDebugUplink.ServerRequested = env.GetBool("debug_uplink");   // owner-side debug -> auto uplink
            // ActivateSession acks FIRST, then hydrates - the ack must reach the server before labels/snapshot requests
            // or the router's handshake gate drops them all ("dropped ... no compatible handshake" spam).
            if (!ActivateSession(env.GetInt("v", 0), env.GetString("build") ?? "", KmhTransportStatus.ChatFallback))
                return;

            // Dial the API with the one-time token this server advertised (nothing dials earlier - see
            // Patch_PM_GlobalData). When the API is coming up it carries the catalog on activation, off the chat carrier.
            bool apiComing = KMHPatchMod.Settings?.UseKmhApiTransport == true && env.GetBool("api_enabled");
            try
            {
                if (apiComing)
                {
                    // Honor the server's policy: if the owner disabled chat fallback, the client must not tunnel features
                    // over chat even if its own setting allows it (old servers omit the flag -> defaults true, unchanged).
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

            // Chat-only session (or the dial didn't take): push the catalog over chat now. When the API is coming up we
            // leave it to that link's activation - and KmhApiClient falls back to a chat push if the API can't connect.
            if (!apiComing) TryHydrateSession();
        }

        // Activate (or refresh) the KMH session once the server is confirmed - shared by the chat hello and the API ack
        // so either transport lights up KMH. Idempotent: the one-time notify/catalog work runs on first activate only,
        // and a live API link is never downgraded to chat.
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
            if (first) { _catalogPushed = false; _snapshotsRequested = false; }   // fresh session: allow one hydration (see TryHydrateSession)
            UI.KmhDashboardState.MarkConfirmed();   // sticky: keep feature buttons visible across a re-handshake
            KmhDispatcher.ServerProtocolVersion = serverVersion;
            KmhDispatcher.ServerBuild = serverBuild ?? "";
            // Ack BEFORE any hydration below, so the server marks this connection compatible before labels/requests
            // arrive (also sent on API activation - it marks the chat path for any fallback packets).
            KmhDispatcher.Send(KmhProtocol.Kind.HelloAck, new { v = KmhProtocol.CurrentVersion });
            if (status == KmhTransportStatus.ApiConnected || KmhTransport.Status != KmhTransportStatus.ApiConnected)
                KmhTransport.Status = status;   // don't downgrade a live API link to chat

            // Once the API link is up, hydrate over it (catalog + full snapshot refresh), kept off the RWT chat
            // carrier. The chat-first activation defers; OnHello's chat-only path and KmhApiClient's API-failure
            // fallback cover the rest.
            if (status == KmhTransportStatus.ApiConnected) TryHydrateSession();

            if (!first) return true;

            string buildLabel = string.IsNullOrEmpty(KmhDispatcher.ServerBuild) ? "<pre-1.1.0>" : KmhDispatcher.ServerBuild;
            KmhLog.Info($"KMH server detected (protocol v{serverVersion}, build '{buildLabel}', via {(status == KmhTransportStatus.ApiConnected ? "API" : "chat")}) - features enabled");
            KmhNotifications.Positive(string.IsNullOrEmpty(KmhDispatcher.ServerName)
                ? $"KMH server connected (protocol v{serverVersion})"
                : $"Connected to KMH server '{KmhDispatcher.ServerName}' (protocol v{serverVersion})");

            if (string.IsNullOrEmpty(KmhDispatcher.ServerBuild))
                KmhNotifications.Neutral("This server runs an older KMH build (pre-1.1.0). New features (auctions, want board, world events) stay hidden until the server owner updates.");
            else if (KmhDispatcher.ServerBuild != KmhProtocol.BuildVersion)
            {
                int cmp = CompareBuilds(KmhDispatcher.ServerBuild, KmhProtocol.BuildVersion);
                if (cmp > 0)
                    KmhNotifications.Neutral($"This server runs a newer KMH build ({KmhDispatcher.ServerBuild}) than your mod ({KmhProtocol.BuildVersion}) - update your KMH Patch to use its newer features.");
                else if (cmp < 0)
                    KmhNotifications.Neutral($"Your KMH Patch ({KmhProtocol.BuildVersion}) is newer than this server ({KmhDispatcher.ServerBuild}) - some features may not work until the owner updates the addon.");
                else
                    KmhNotifications.Neutral($"KMH build differs - server {KmhDispatcher.ServerBuild}, your mod {KmhProtocol.BuildVersion}. Update so both sides match.");
            }

            string endpoint = string.IsNullOrEmpty(TCPNetwork.Network.Ip) ? "" : $"{TCPNetwork.Network.Ip}:{TCPNetwork.Network.Port}";
            Extensibility.KmhClientEventBus.Instance.RaiseKmhServerConnected(
                new KMH.Sdk.Client.Events.KmhServerConnectedEvent { ServerProtocolVersion = serverVersion, Endpoint = endpoint });

            // Remember this KMH server + its versions for the local server directory (RWT version = ours, matched at login).
            try
            {
                string rwt = ""; try { rwt = CommonValues.ExecutableVersion ?? ""; } catch { }
                Features.Servers.SeenServersStore.Record(endpoint, KmhDispatcher.ServerBuild, rwt);
            }
            catch (Exception ex) { KmhLog.Warn($"Seen-servers record threw: {ex.Message}"); }

            // (Cache hydration - RequestAll - is deferred to TryHydrateSession so its dozen requests ride the API
            // transport instead of flooding the chat carrier during the pre-API handshake window.)
            return true;
        }

        // Session hydration - the item catalog (labels + base values + condition defs, several chunks) AND a full
        // snapshot refresh (RequestAll's dozen requests) - is deferred to whichever transport actually carries it: the
        // API activation (preferred), OnHello's chat-only path, or KmhApiClient's chat fallback when the API can't be
        // reached. Firing it the instant the chat hello lands used to dump ~20 packets onto the RWT chat carrier before
        // the API transport finished connecting. Each half is guarded to run once per session (catalog latches only on
        // a real send, so a premature/refused attempt can retry).
        private static bool _catalogPushed;
        private static bool _snapshotsRequested;

        internal static void TryHydrateSession()
        {
            TryPushCatalog();
            if (!_snapshotsRequested)
            {
                _snapshotsRequested = true;
                try { KmhRefresh.RequestAll(); }
                catch (Exception ex) { KmhLog.Warn($"KMH join hydration threw: {ex.Message}"); }
            }
        }

        private static void TryPushCatalog()
        {
            if (_catalogPushed) return;
            bool ok = false;
            try { ok = Features.Catalog.ItemLabelsSender.PushOnce(); }
            catch (Exception ex) { KmhLog.Warn($"ItemLabels: catalog push threw: {ex.Message}"); }
            if (ok) _catalogPushed = true;
        }

        // Compare KMH build strings ("1.1.0"). >0 server newer, <0 client newer, 0 if equal or unparseable.
        private static int CompareBuilds(string serverBuild, string clientBuild)
            => Version.TryParse(serverBuild, out Version sv) && Version.TryParse(clientBuild, out Version cv)
                ? sv.CompareTo(cv) : 0;

        // Server-pushed transient toast { level, text }. Feature handlers on the server use it for action feedback
        // (e.g. a failed guild invite)
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
