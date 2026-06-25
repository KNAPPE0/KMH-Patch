using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;

namespace KMHPatch.SubProtocol
{
    // Central router for KMH sub-protocol messages. Inbound: Patch_PM_Chat_KmhIntercept calls Receive() with the
    // envelope; we dispatch by Kind. Outbound: Send(kind, data) serializes an envelope into a PKT_Chat tagged so the
    // server router recognizes it. IsKmhServer flips true after the server's kmh.hello - until then we send nothing,
    // or a patched client on a stock RWT server would broadcast KMH JSON as visible chat.
    public static class KmhDispatcher
    {
        private static readonly Dictionary<string, Action<KmhEnvelope>> Handlers
            = new Dictionary<string, Action<KmhEnvelope>>();

        public static bool IsKmhServer { get; internal set; } = false;
        public static int  ServerProtocolVersion { get; internal set; } = 0;

        // Server's human-readable release (from kmh.hello). Empty = pre-1.1.0 server that doesn't advertise a build.
        public static string ServerBuild { get; internal set; } = "";

        // True once we've confirmed the server is at least this client's build, i.e. it speaks the v1.1.0 feature
        // set (auctions / want board / world events). A pre-1.1.0 server leaves ServerBuild empty.
        public static bool ServerSupportsCurrentBuild
            => !string.IsNullOrEmpty(ServerBuild) && ServerBuild == KmhProtocol.BuildVersion;

        // Cheapest possible "is the protocol alive?" signal. Updated by every successful Receive - feature UI /
        // diagnostics surfaces can read it without subscribing to anything. Cleared by ResetSession on disconnect
        public static string   LastReceivedKind { get; private set; }
        public static DateTime LastReceivedAt   { get; private set; } = DateTime.MinValue;
        public static int      ReceivedCount    { get; private set; }

        public static void RegisterHandler(string kind, Action<KmhEnvelope> handler)
        {
            if (string.IsNullOrEmpty(kind))
            {
                KmhLog.Warn("RegisterHandler called with null/empty kind");
                return;
            }
            // Surface accidental clobbers - core handlers register once each at bootstrap, so a duplicate signals a
            // bug or an extension reaching past the SDK guard
            if (Handlers.ContainsKey(kind))
                KmhLog.Warn($"Handler for kind '{kind}' is being overwritten - previous registration replaced");
            Handlers[kind] = handler;
        }

        // True if a handler is already registered for this kind. Used by the SDK host to refuse extension
        // registrations that would collide with a core kmh.* handler or with an earlier extension's kind
        public static bool IsRegistered(string kind)
            => !string.IsNullOrEmpty(kind) && Handlers.ContainsKey(kind);

        // Called from the chat-intercept Harmony patch when an inbound message is identified as KMH protocol. Never
        // throws - handler errors are swallowed and logged so a bad message can't crash the chat pipeline
        internal static void Receive(KmhEnvelope env)
        {
            if (env == null || string.IsNullOrEmpty(env.Kind)) return;

            // Update diagnostics counters BEFORE dispatching - a handler exception shouldn't lose the fact that we
            // received the envelope
            LastReceivedKind = env.Kind;
            LastReceivedAt   = DateTime.UtcNow;
            ReceivedCount   += 1;

            if (!Handlers.TryGetValue(env.Kind, out Action<KmhEnvelope> handler))
            {
                // Unknown kind - likely a feature the server is using that the patch hasn't ported yet. Not an
                // error; just log at low priority
                KmhLog.Info($"No handler for kind '{env.Kind}' (server v{env.Version})");
                return;
            }

            try
            {
                handler(env);
            }
            catch (Exception ex)
            {
                KmhLog.Error($"Handler '{env.Kind}' threw", ex);
            }
        }

        // Send a typed message to the server. Silently drops if we haven't yet verified the server speaks KMH - see
        // IsKmhServer comment above
        public static bool Send(string kind, object data)
        {
            if (!IsKmhServer)
            {
                KmhLog.Warn($"Refusing to send '{kind}' - server has not announced KMH support yet");
                return false;
            }

            KmhEnvelope env = new KmhEnvelope(kind, data);

            // prefer the API transport when connected; else chat
            if (KmhApiClient.TrySend(env)) return true;

            if (Network.ServerEndpoint == null)
            {
                KmhLog.Warn($"Refusing to send '{kind}' - no active server connection");
                return false;
            }

            PKT_Chat pkt = new PKT_Chat
            {
                Username  = KmhProtocol.ClientUsername,
                Message   = env.Serialize(),
                IsCommand = false,
            };

            // Send straight via the TCP layer; do NOT route through PM_Chat.SendMessage (it plays a UI sound and
            // sets IsCommand based on '/' prefix - neither is correct for protocol traffic). Guard the enqueue so a
            // connection dropped mid-send surfaces as a failed Send, not an exception bubbling up through whatever
            // UI button triggered it
            try
            {
                Network.ServerEndpoint.EnqueuePacket(RwtCompat.ChatHeader, pkt);
                return true;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Send '{kind}' failed: {ex.Message}");
                return false;
            }
        }

        // Reset on disconnect so a fresh connection re-runs the handshake. Called from
        // Patch_DisconnectionManager_KmhDisconnect (on RWT disconnects of any cause) and from
        // Patch_PM_GlobalData_KmhConnected (at the start of every new session, to clear any leftover state)
        internal static void ResetSession()
        {
            if (IsKmhServer || ServerProtocolVersion > 0)
            {
                KmhLog.Info($"Resetting KMH session state (was IsKmhServer={IsKmhServer}, v={ServerProtocolVersion})");
            }
            IsKmhServer = false;
            ServerProtocolVersion = 0;
            ServerBuild = "";
            KmhApiClient.Disconnect();   // drop the KMH API link too, if it was up
            KmhTransport.Status = KmhTransportStatus.Offline;

            // Clear diagnostics so a fresh session doesn't show stale state.
            LastReceivedKind = null;
            LastReceivedAt   = DateTime.MinValue;
            ReceivedCount    = 0;
        }
    }
}
