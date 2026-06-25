using System;
using GameClient.Misc;
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
            if (!ActivateSession(env.GetInt("v", 0), env.GetString("build") ?? "", KmhTransportStatus.ChatFallback))
                return;

            KmhDispatcher.Send(KmhProtocol.Kind.HelloAck, new { v = KmhProtocol.CurrentVersion });

            // The client also dials the API directly on RWT-connect (Patch_PM_GlobalData), so only connect here if that
            // hasn't already brought it up - this lets the stronger chat-issued token be used when chat works.
            try
            {
                if (KMHPatchMod.Settings?.UseKmhApiTransport == true && env.GetBool("api_enabled") && !KmhApiClient.Active)
                {
                    string host = string.IsNullOrEmpty(KMHPatchMod.Settings.KmhApiHostOverride)
                        ? TCPNetwork.Network.Ip : KMHPatchMod.Settings.KmhApiHostOverride;
                    int port = env.GetInt("api_port", KMHPatchMod.Settings.KmhApiPort);
                    if (port <= 0) port = KMHPatchMod.Settings.KmhApiPort;
                    KmhApiClient.Connect(host, port, SessionHandler.Username, env.GetString("api_token") ?? "",
                        KMHPatchMod.Settings.AllowChatTransportFallback);
                }
            }
            catch (Exception ex) { KmhLog.Warn($"KMH API: connect attempt threw: {ex.Message}"); }
        }

        // Activate (or refresh) the KMH session once the server is confirmed - shared by the chat hello and the API ack
        // so either transport lights up KMH. Idempotent: the one-time notify/catalog work runs on first activate only,
        // and a live API link is never downgraded to chat.
        internal static bool ActivateSession(int serverVersion, string serverBuild, KmhTransportStatus status)
        {
            if (serverVersion != KmhProtocol.CurrentVersion)
            {
                KmhLog.Warn($"Server KMH version {serverVersion} does not match client {KmhProtocol.CurrentVersion} - disabling KMH features for this session");
                KmhDispatcher.IsKmhServer = false;
                KmhTransport.Status = KmhTransportStatus.VersionMismatch;
                return false;
            }

            bool first = !KmhDispatcher.IsKmhServer;
            KmhDispatcher.IsKmhServer = true;
            KmhDispatcher.ServerProtocolVersion = serverVersion;
            KmhDispatcher.ServerBuild = serverBuild ?? "";
            if (status == KmhTransportStatus.ApiConnected || KmhTransport.Status != KmhTransportStatus.ApiConnected)
                KmhTransport.Status = status;   // don't downgrade a live API link to chat

            if (!first) return true;

            string buildLabel = string.IsNullOrEmpty(KmhDispatcher.ServerBuild) ? "<pre-1.1.0>" : KmhDispatcher.ServerBuild;
            KmhLog.Info($"KMH server detected (protocol v{serverVersion}, build '{buildLabel}', via {(status == KmhTransportStatus.ApiConnected ? "API" : "chat")}) - features enabled");
            KmhNotifications.Positive($"KMH server connected (protocol v{serverVersion})");

            if (string.IsNullOrEmpty(KmhDispatcher.ServerBuild))
                KmhNotifications.Neutral("This server runs an older KMH build (pre-1.1.0). New features (auctions, want board, world events) stay hidden until the server owner updates.");
            else if (KmhDispatcher.ServerBuild != KmhProtocol.BuildVersion)
                KmhNotifications.Neutral($"KMH version mismatch - server is {KmhDispatcher.ServerBuild}, your mod is {KmhProtocol.BuildVersion}. Update so both sides match for full compatibility.");

            string endpoint = string.IsNullOrEmpty(TCPNetwork.Network.Ip) ? "" : $"{TCPNetwork.Network.Ip}:{TCPNetwork.Network.Port}";
            Extensibility.KmhClientEventBus.Instance.RaiseKmhServerConnected(
                new KMH.Sdk.Client.Events.KmhServerConnectedEvent { ServerProtocolVersion = serverVersion, Endpoint = endpoint });

            try { Features.Catalog.ItemLabelsSender.PushOnce(); }
            catch (Exception ex) { KmhLog.Warn($"ItemLabels: push at handshake threw: {ex.Message}"); }
            return true;
        }

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
