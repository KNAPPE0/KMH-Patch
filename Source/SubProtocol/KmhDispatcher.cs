using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;

namespace KMHPatch.SubProtocol
{
    // Nothing sends before the server's kmh.hello, or a patched client on a stock RWT server broadcasts KMH JSON as visible chat.
    public static class KmhDispatcher
    {
        private static readonly Dictionary<string, Action<KmhEnvelope>> Handlers
            = new Dictionary<string, Action<KmhEnvelope>>();

        public static bool IsKmhServer { get; internal set; } = false;
        public static int  ServerProtocolVersion { get; internal set; } = 0;

        // Empty on a pre-1.1.0 server, which does not advertise a build.
        public static string ServerBuild { get; internal set; } = "";

        // Empty on a pre-1.2.0 server, which does not advertise a name.
        public static string ServerName { get; internal set; } = "";

        public static bool ServerSupportsCurrentBuild
            => !string.IsNullOrEmpty(ServerBuild) && ServerBuild == KmhProtocol.BuildVersion;

        public static string   LastReceivedKind { get; private set; }
        public static DateTime LastReceivedAt   { get; private set; } = DateTime.MinValue;
        public static int      ReceivedCount    { get; private set; }

        // Unknown inbound kinds already logged - once each, not once per packet.
        private static readonly System.Collections.Generic.HashSet<string> _unknownKindsLogged
            = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        public static void RegisterHandler(string kind, Action<KmhEnvelope> handler)
        {
            if (string.IsNullOrEmpty(kind))
            {
                KmhLog.Warn("RegisterHandler called with null/empty kind");
                return;
            }
            // Core handlers register once each at bootstrap, so a duplicate means a bug or an extension past the SDK guard.
            if (Handlers.ContainsKey(kind))
                KmhLog.Warn($"Handler for kind '{kind}' is being overwritten - previous registration replaced");
            Handlers[kind] = handler;
        }

        // The SDK host refuses an extension registration that would collide with a core kind or an earlier extension's.
        public static bool IsRegistered(string kind)
            => !string.IsNullOrEmpty(kind) && Handlers.ContainsKey(kind);

        // Never throws: a handler error is logged and swallowed so a bad message cannot crash the chat pipeline.
        internal static void Receive(KmhEnvelope env)
        {
            if (env == null || string.IsNullOrEmpty(env.Kind)) return;

            // Reassembled here, so no feature handler has to know its envelope was split.
            if (KmhFragments.IsFragment(env.Kind))
            {
                KmhEnvelope whole = KmhFragments.Accept(env);
                if (whole == null) return;
                Receive(whole);
                return;
            }

            // Before dispatching, so a handler exception cannot lose the fact that the envelope arrived.
            LastReceivedKind = env.Kind;
            LastReceivedAt   = DateTime.UtcNow;
            ReceivedCount   += 1;

            if (!Handlers.TryGetValue(env.Kind, out Action<KmhEnvelope> handler))
            {
                // Not an error, usually a feature this patch has not ported yet, so log once per kind.
                if (_unknownKindsLogged.Add(env.Kind))
                    KmhLog.Info($"No handler for kind '{env.Kind}' (server v{env.Version}) - further packets of this kind are ignored silently");
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

        // The server rejects a chat envelope past this, so the client has to split at the same ceiling.
        internal const int MaxChatEnvelopeBytes = 64 * 1024;

        // The handshake always rides chat - it is how the API is discovered; the rest is feature traffic an owner may forbid there.
        internal static bool IsTransportControlKind(string kind)
            => kind == KmhProtocol.Kind.Hello || kind == KmhProtocol.Kind.HelloAck
            || kind == KmhProtocol.Kind.Ping  || kind == KmhProtocol.Kind.Pong;

        internal static bool ChatMayCarry(string kind)
            => IsTransportControlKind(kind) || KmhApiClient.ChatFallbackAllowed;

        // Only NotConnected proves nothing was written; an ambiguous write is repeatable only under an op id the server dedups.
        internal static bool MayRetryOnChat(KmhSendResult r) => r == KmhSendResult.NotConnected;

        internal static bool MayRetryOnChat(KmhSendResult r, bool idempotent)
            => MayRetryOnChat(r) || (idempotent && r == KmhSendResult.AmbiguousIoFailure);

        public static bool Send(string kind, object data) => Send(kind, data, null);

        // opId names the logical action: pass the same one when re-sending the same action, never a fresh one.
        public static bool Send(string kind, object data, string opId)
        {
            if (!IsKmhServer)
            {
                KmhLog.Warn($"Refusing to send '{kind}' - server has not announced KMH support yet");
                return false;
            }

            KmhEnvelope env = new KmhEnvelope(kind, data, opId: opId);

            // The API transport when it is up, chat otherwise.
            KmhSendResult api = KmhApiClient.Send(env);
            if (api == KmhSendResult.Sent) return true;

            // Without an op id, a chat retry turns one unlucky socket error into a second withdrawal, bid or purchase.
            if (!MayRetryOnChat(api, !string.IsNullOrEmpty(opId)))
            {
                KmhLog.Warn($"Send '{kind}' could not be completed ({api}) - not retried over chat, because the server may already have it. Try again if nothing happens.");
                return false;
            }

            if (!ChatMayCarry(kind))
            {
                KmhLog.Warn($"Send '{kind}' dropped - the KMH API link is down and this server does not allow KMH features over RWT chat.");
                return false;
            }

            if (Network.ServerEndpoint == null)
            {
                KmhLog.Warn($"Refusing to send '{kind}' - no active server connection");
                return false;
            }

            string wire = env.Serialize();
            // Never through PM_Chat.SendMessage: it plays a UI sound and sets IsCommand from a '/' prefix.
            try
            {
                // Same fragmentation the API path uses, so a large request is not limited to whichever transport is up.
                if (wire.Length > MaxChatEnvelopeBytes)
                {
                    List<KmhEnvelope> parts = KmhFragments.Split(kind, wire, MaxChatEnvelopeBytes);
                    if (parts == null) return false;
                    foreach (KmhEnvelope part in parts)
                        Network.ServerEndpoint.EnqueuePacket(RwtCompat.ChatHeader, new PKT_Chat
                        { Username = KmhProtocol.ClientUsername, Message = part.Serialize(), IsCommand = false });
                    KmhLog.Protocol($"Send '{kind}' went over chat as {parts.Count} fragment(s), {wire.Length} logical bytes.");
                    return true;
                }

                Network.ServerEndpoint.EnqueuePacket(RwtCompat.ChatHeader, new PKT_Chat
                { Username = KmhProtocol.ClientUsername, Message = wire, IsCommand = false });
                return true;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Send '{kind}' failed: {ex.Message}");
                return false;
            }
        }

        // Bumped on every session end: a window or in-flight request from an older generation must not act on the next connection.
        internal static int SessionGeneration { get; private set; }

        internal static void ResetSession()
        {
            SessionGeneration++;
            if (IsKmhServer || ServerProtocolVersion > 0)
            {
                KmhLog.Info($"Resetting KMH session state (was IsKmhServer={IsKmhServer}, v={ServerProtocolVersion})");
            }
            IsKmhServer = false;
            UI.KmhDashboardState.ResetForNewConnection();   // new connection: capabilities are unknown until the next hello
            KmhCapabilities.Reset();                        // re-inferred from the next server's manifest (or its absence)
            ServerProtocolVersion = 0;
            ServerBuild = "";
            ServerName = "";
            KmhApiClient.Disconnect();   // drop the KMH API link too, if it was up
            KmhTransport.Status = KmhTransportStatus.Offline;
            KmhTransport.DegradedReason = null;   // scoped to one server, like everything else reset here
            KmhOpId.Clear();             // held ids mean nothing to the next server, and must not gate its first clicks
            // Connect as well as disconnect, or a reconnect keeps the previous server's consent and its queued lines.
            Diagnostics.KmhDebugUplink.ResetForNewServer();
            Features.Delivery.GameComponent_KMHDeliveryReceipts.Instance?.ForgetAcksForNewSession();

            LastReceivedKind = null;
            LastReceivedAt   = DateTime.MinValue;
            ReceivedCount    = 0;
        }
    }
}
