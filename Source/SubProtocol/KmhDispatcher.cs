using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;

namespace KMHPatch.SubProtocol
{
    // Nothing sends before kmh.hello, or a patched client on a stock RWT server broadcasts KMH JSON as visible chat.
    public static class KmhDispatcher
    {
        private static readonly Dictionary<string, Action<KmhEnvelope>> Handlers
            = new Dictionary<string, Action<KmhEnvelope>>();

        public static bool IsKmhServer { get; internal set; } = false;
        public static int  ServerProtocolVersion { get; internal set; } = 0;

        // The far end's own words, shown in notifications and history, so they are made inert once, here.
        internal const int MaxServerName = 80, MaxServerBuild = 64;

        // Empty on an older server, which does not advertise a build.
        public static string ServerBuild
        {
            get => _serverBuild;
            internal set => _serverBuild = UI.KmhDisplayText.InertLine(value ?? "", MaxServerBuild);
        }
        private static string _serverBuild = "";

        // Empty on an older server, which does not advertise a name.
        public static string ServerName
        {
            get => _serverName;
            internal set => _serverName = UI.KmhDisplayText.InertLine(value ?? "", MaxServerName);
        }
        private static string _serverName = "";

        // Empty on an older server, so history keyed on it tolerates records without one.
        public static string ServerId { get; internal set; } = "";

        public static bool ServerSupportsCurrentBuild
            => !string.IsNullOrEmpty(ServerBuild) && ServerBuild == KmhProtocol.BuildVersion;

        public static string   LastReceivedKind { get; private set; }
        public static DateTime LastReceivedAt   { get; private set; } = DateTime.MinValue;
        public static int      ReceivedCount    { get; private set; }

        private static readonly System.Collections.Generic.HashSet<string> _unknownKindsLogged
            = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        internal const int MaxUnknownKindsLogged = 64;

        private static string KmhLogSafe(string kind) => kind.Length > 64 ? kind.Substring(0, 64) + "…" : kind;

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
            Features.Reports.KmhRequestLog.Live.Received(env);

            if (!Handlers.TryGetValue(env.Kind, out Action<KmhEnvelope> handler))
            {
                // Once per kind, and bounded: the server chooses the kinds and each one is kept all session.
                if (_unknownKindsLogged.Count < MaxUnknownKindsLogged && _unknownKindsLogged.Add(env.Kind))
                    KmhLog.Info($"No handler for kind '{KmhLogSafe(env.Kind)}' (server v{env.Version}) - further packets of this kind are ignored silently");
                return;
            }

            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                handler(env);
            }
            catch (Exception ex)
            {
                KmhLog.Error($"Handler '{env.Kind}' threw", ex);
            }
            Diagnostics.KmhSlowWork.Note(env.Kind, System.Diagnostics.Stopwatch.GetTimestamp() - started);
        }

        // The server rejects a chat envelope past this, so the client has to split at the same ceiling.
        internal const int MaxChatEnvelopeBytes = 64 * 1024;

        // The handshake always rides chat, since that is how the API is discovered; an owner may forbid the rest there.
        internal static bool IsTransportControlKind(string kind)
            => Features.Enforcement.EnforcementAdmission.IsControl(kind)
            || kind == KmhProtocol.Kind.Hello || kind == KmhProtocol.Kind.HelloAck
            || kind == KmhProtocol.Kind.Ping  || kind == KmhProtocol.Kind.Pong;

        internal static bool ChatMayCarry(string kind)
            => IsTransportControlKind(kind) || KmhApiClient.ChatFallbackAllowed;

        // Only NotConnected proves nothing was written; an ambiguous write is repeatable only under an op id the server dedups.
        internal static bool MayRetryOnChat(KmhSendResult r) => r == KmhSendResult.NotConnected;

        internal static bool MayRetryOnChat(KmhSendResult r, bool idempotent)
            => MayRetryOnChat(r) || (idempotent && r == KmhSendResult.AmbiguousIoFailure);

        public static bool Send(string kind, object data) => Send(kind, data, null);

        // Self-test fixtures reach real paths that send, and a fixture Incompatible frame deadlocks an enforced join.
        // Checked before the Incompatible exemption, which bypasses the IsKmhServer gate.
        internal static bool SuppressSendForTest;

        // Lets a test assert a self-test run is silent rather than merely believe it.
        internal static int SuppressedSendCount;

        // opId names the logical action: pass the same one when re-sending the same action, never a fresh one.
        public static bool Send(string kind, object data, string opId)
        {
            if (SuppressSendForTest)
            {
                SuppressedSendCount++;
                KmhLog.Debug($"Self-test: '{kind}' was not transmitted - the suite never talks to a live server.");
                return false;
            }
            bool sent = Transmit(kind, data, opId);
            Features.Reports.KmhRequestLog.Live.Sent(kind, opId, sent);
            return sent;
        }

        private static bool Transmit(string kind, object data, string opId)
        {
            if (Features.Enforcement.EnforcementAdmission.Pending)
            {
                // Logged: a silent drop here makes a failed catalog push look like a transport fault.
                if (!Features.Enforcement.EnforcementAdmission.IsControl(kind))
                {
                    KmhLog.Debug($"Send '{kind}' held: admission is still pending, so only control messages go.");
                    return false;
                }
                data = Features.Enforcement.EnforcementAdmission.Bind(data);
            }
            // Exempt: it says we are NOT treating the peer as KMH; gating it leaves an enforcing server on a timeout.
            if (!IsKmhServer && kind != KmhProtocol.Kind.Incompatible)
            {
                KmhLog.Warn($"Refusing to send '{kind}' - server has not announced KMH support yet");
                return false;
            }

            KmhEnvelope env = new KmhEnvelope(kind, data, opId: opId);

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

        // Bumped on every session end: a window or request from an older generation must not act on the next connection.
        internal static int SessionGeneration { get; private set; }

        // Set when this connection echoed the client's own envelope back as chat: a stock RWT server, so nothing it relays
        // under the system name is the server's.
        internal static bool StockServerSeen { get; private set; }

        internal static void MarkStockServer() => StockServerSeen = true;

        internal static void ResetSession()
        {
            Features.Enforcement.EnforcementAdmission.Reset();
            StockServerSeen = false;
            SessionGeneration++;
            if (IsKmhServer || ServerProtocolVersion > 0)
            {
                KmhLog.Info($"Resetting KMH session state (was IsKmhServer={IsKmhServer}, v={ServerProtocolVersion})");
            }
            IsKmhServer = false;
            UI.KmhDashboardState.ResetForNewConnection();
            KmhCapabilities.Reset();
            ServerProtocolVersion = 0;
            ServerBuild = "";
            ServerName = "";
            ServerId = "";
            KmhApiClient.Disconnect();
            KmhTransport.Status = KmhTransportStatus.Offline;
            KmhTransport.DegradedReason = null;
            KmhOpId.Clear();             // held ids mean nothing to the next server, and must not gate its first clicks
            // Connect as well as disconnect, or a reconnect keeps the previous server's consent and its queued lines.
            Diagnostics.KmhDebugUplink.ResetForNewServer();
            Features.Reports.KmhReportClient.ResetSession();
            Features.Delivery.GameComponent_KMHDeliveryReceipts.Instance?.ForgetAcksForNewSession();

            LastReceivedKind = null;
            LastReceivedAt   = DateTime.MinValue;
            ReceivedCount    = 0;

            // Or Discord keeps naming the server just left. Field writes only: this runs on the network thread,
            // so Steam's Join Game is withdrawn by the frame pump, never by calling Steamworks from here.
            Features.Discord.KmhPresenceActivity.Clear();
            Features.Discord.KmhRichPresence.InvalidateNow();
            Features.Friends.KmhPresenceReporter.Invalidate();
        }
    }
}
