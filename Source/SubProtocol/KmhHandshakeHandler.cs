using System;
using KMHPatch.Diagnostics;
using KMHPatch.Notifications;

namespace KMHPatch.SubProtocol
{
    // First and only built-in handler shipped with the dispatcher itself. Everything else (treasury, marketplace,
    // quests...) registers its own handlers as those features port over
    //
    // The handshake flips KmhDispatcher.IsKmhServer to true once the server announces compatible KMH support, which
    // is the gate that allows outbound KMH traffic. Without this, a patched client connected to a stock RWT server
    // would never reach the .Send() path - fail-safe by design
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

        private static void OnHello(KmhEnvelope env)
        {
            int serverVersion = env.GetInt("v", 0);

            // For now we only support exact-match on protocol version. Loosen to "compatible range" once we have
            // more than one shipped version
            if (serverVersion != KmhProtocol.CurrentVersion)
            {
                KmhLog.Warn(
                    $"Server KMH version {serverVersion} does not match " +
                    $"client {KmhProtocol.CurrentVersion} - disabling KMH features for this session"
                );
                KmhDispatcher.IsKmhServer = false;
                return;
            }

            KmhDispatcher.IsKmhServer = true;
            KmhDispatcher.ServerProtocolVersion = serverVersion;
            KmhLog.Info($"KMH server detected (protocol v{serverVersion}) - features enabled");

            // Visible confirmation for the player. Flash (not Letter) because it's transient - the persistent state
            // is shown in the KMH tab
            KmhNotifications.Positive($"KMH server connected (protocol v{serverVersion})");

            // Acknowledge so server-side can log the successful handshake too.
            KmhDispatcher.Send(KmhProtocol.Kind.HelloAck, new { v = KmhProtocol.CurrentVersion });

            // Tell client extensions the KMH session is live (subscriptions are wired at startup, so they're in
            // place by now)
            string endpoint = string.IsNullOrEmpty(TCPNetwork.Network.Ip)
                ? "" : $"{TCPNetwork.Network.Ip}:{TCPNetwork.Network.Port}";
            Extensibility.KmhClientEventBus.Instance.RaiseKmhServerConnected(
                new KMH.Sdk.Client.Events.KmhServerConnectedEvent
                {
                    ServerProtocolVersion = serverVersion,
                    Endpoint              = endpoint
                });

            // Push our local DefDatabase item catalog so the server can resolve friendly labels for Discord-side
            // commands. Best- effort - failure is logged and the rest of the session continues using raw defNames
            // for Discord output
            try { Features.Catalog.ItemLabelsSender.PushOnce(); }
            catch (Exception ex) { KmhLog.Warn($"ItemLabels: push at handshake threw: {ex.Message}"); }
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
                KmhLog.Info($"Pong received from server (round-trip {ms:F1} ms)");
            }
            else
            {
                // Pong without a matching SendPing - probably a server-initiated heartbeat or a stale reply. Still
                // worth logging
                KmhLog.Info("Pong received from server (unsolicited)");
            }
        }
    }
}
