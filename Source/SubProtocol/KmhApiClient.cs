using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;

namespace KMHPatch.SubProtocol
{
    // What a caller must know about a send it may be about to repeat somewhere else.
    public enum KmhSendResult
    {
        Sent,
        NotConnected,           // nothing was written, so the same request may safely go over another transport
        TooLarge,
        AmbiguousIoFailure,     // a write threw part-way: the server may or may not hold this request
        SerializationFailure,
    }

    // Client-side KMH API transport; uses direct TCP when enabled/advertised, otherwise falls back to chat.
    public static class KmhApiClient
    {
        private const string KindApiHello    = "kmh.api.hello";
        private const string KindApiHelloAck = "kmh.api.hello.ack";
        private const string KindPing        = "kmh.ping";
        private const string KindPong        = "kmh.pong";

        private const int MaxFrameBytes      = 64 * 1024;
        private const int ConnectTimeoutMs   = 5_000;
        private const int HelloAckTimeoutMs  = 10_000;   // a peer that accepts TCP and then says nothing
        private const int IdleTimeoutMs      = 40_000;   // drop + reconnect if the server goes silent past a heartbeat
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

        // One per Connect and shares nothing, so an older attempt still unwinding cannot publish into the session that replaced it.
        private sealed class Link
        {
            public int    Generation;
            public string Host;
            public int    Port;
            public string User;
            public string Token;             // rotated by each successful ack, so a reconnect has its own credential
            public bool   AllowChatFallback;
            public CancellationTokenSource Cts;
            public TcpClient     Socket;     // the socket this attempt owns, closed on invalidation
            public NetworkStream Stream;     // non-null only while this link's API is up
            public string LastFailReason;    // warn once per DISTINCT failure; repeats go to the protocol log
            public readonly object SendLock = new object();
        }

        private static volatile Link _current;
        private static int _generation;

        public static bool Active
        {
            get { Link l = _current; return l != null && !l.Cts.IsCancellationRequested; }
        }

        // What this session is dialling right now, which is rarely what the settings say - the server's word wins.
        public static string EffectiveEndpoint
        {
            get { Link l = _current; return l == null ? "" : $"{l.Host}:{l.Port}"; }
        }

        // Only the live attempt may publish status, activate a session, or claim the transport.
        private static bool IsCurrent(Link l) => l != null && ReferenceEquals(_current, l) && !l.Cts.IsCancellationRequested;

        // Apply the server's chat-fallback policy to a live link (the early dial may have used the local setting).
        public static void SetChatFallbackAllowed(bool allowed)
        {
            Link l = _current;
            if (l != null) l.AllowChatFallback = allowed;
        }

        public static bool ChatFallbackAllowed
        {
            get { Link l = _current; return l == null || l.AllowChatFallback; }
        }

        public static void Connect(string host, int port, string username, string token, bool allowChatFallback)
        {
            Disconnect();
            if (string.IsNullOrEmpty(host) || port <= 0) { KmhLog.Warn("KMH API: no host/port - staying on chat."); return; }
            Link link = new Link
            {
                Generation = Interlocked.Increment(ref _generation),
                Host = host, Port = port,
                User = username ?? "", Token = token ?? "",
                AllowChatFallback = allowChatFallback,
                Cts = new CancellationTokenSource(),
            };
            _current = link;
            KmhTransport.Status = KmhTransportStatus.ApiConnecting;
            KmhTransport.DegradedReason = null;   // a new dial: the last server's failure is not this one's
            KmhLog.Info($"KMH API: transport on - dialing {host}:{port} as {(string.IsNullOrEmpty(link.User) ? "?" : link.User)}.");
            Task.Run(() => RunLoop(link));
        }

        public static void Disconnect()
        {
            Link l = _current;
            _current = null;
            if (l == null) return;
            try { l.Cts.Cancel(); } catch { }
            // Closed here rather than left to the read: a socket read can outlive its cancellation by a whole idle timeout.
            Invalidate(l);
        }

        private static void Invalidate(Link l)
        {
            l.Stream = null;
            try { l.Socket?.Close(); } catch { }
        }

        private static async Task RunLoop(Link link)
        {
            CancellationToken ct = link.Cts.Token;
            int backoffMs = 1_000;
            while (!ct.IsCancellationRequested && IsCurrent(link))
            {
                try
                {
                    // Reset the ladder ONLY on a real link, so a rejecting server backs off instead of retrying at 1s.
                    if (await ServeOnce(link, ct).ConfigureAwait(false)) backoffMs = 1_000;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!IsCurrent(link)) break;
                    NoteFailure(link, ex.Message,
                        $"KMH API: {link.Host}:{link.Port} unreachable ({ex.Message}) - {(link.AllowChatFallback ? "staying on RWT chat" : "offline")}. Retrying in the background.");
                    if (link.AllowChatFallback && KmhTransport.Status != KmhTransportStatus.ApiConnected)
                    {
                        KmhTransport.Status = KmhTransportStatus.ChatFallback; // chat still serves features
                        KmhTransport.DegradedReason = $"{link.Host}:{link.Port} - {ex.Message}";
                    }
                }
                if (!IsCurrent(link)) break;
                // An attempt that ended without the link up still has to hydrate, so it goes over chat.
                if (link.Stream == null && link.AllowChatFallback)
                    KmhMainThread.Post(() => { if (IsCurrent(link)) KmhHandshakeHandler.TryHydrateSession(); });

                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(backoffMs, ct).ConfigureAwait(false); } catch { break; }
                backoffMs = Math.Min(backoffMs * 2, 30_000);
            }
            Invalidate(link);
        }

        private static void NoteFailure(Link link, string reason, string message)
        {
            if (!string.Equals(link.LastFailReason, reason ?? "", StringComparison.Ordinal))
            {
                link.LastFailReason = reason ?? "";
                KmhLog.Warn(message);
            }
            else KmhLog.Protocol(message);
        }

        private static async Task<bool> ServeOnce(Link link, CancellationToken ct)
        {
            using (TcpClient client = new TcpClient())
            {
                link.Socket = client;
                Task connect = client.ConnectAsync(link.Host, link.Port);
                if (await Task.WhenAny(connect, Task.Delay(ConnectTimeoutMs, ct)).ConfigureAwait(false) != connect)
                    throw new TimeoutException($"connect to {link.Host}:{link.Port} timed out");
                await connect.ConfigureAwait(false); // surface connect errors
                client.NoDelay = true;

                using (NetworkStream stream = client.GetStream())
                {
                    KmhLog.Protocol($"KMH API: connected to {link.Host}:{link.Port}, sending hello.");
                    // Declares what this client can read, so a server with a larger frame never sends one we would reject.
                    await WriteFrame(stream, KindApiHello, new
                    {
                        v = KmhProtocol.CurrentVersion, build = KmhProtocol.BuildVersion,
                        username = link.User, token = link.Token,
                        max_frame_kb = MaxFrameBytes / 1024, supports_fragmentation = true,
                    }, ct).ConfigureAwait(false);

                    // Bounded: a peer that connects and then goes quiet would hold this attempt open forever, and the backoff ladder never runs.
                    KmhEnvelope ack = await ReadFrameWithin(stream, HelloAckTimeoutMs, ct).ConfigureAwait(false);
                    if (ack == null || ack.Kind != KindApiHelloAck || !ack.GetBool("ok"))
                    {
                        if (!IsCurrent(link)) return false;
                        string reason = ack?.GetString("reason") ?? "no ack";
                        KmhTransport.Status = reason == "auth" ? KmhTransportStatus.AuthFailed
                            : (link.AllowChatFallback ? KmhTransportStatus.ChatFallback : KmhTransportStatus.Offline);
                        NoteFailure(link, reason, $"KMH API: handshake rejected ({reason}) - {(link.AllowChatFallback ? "falling back to chat." : "offline.")}");
                        return false;   // link never came up: keep chat serving features and let the backoff grow
                    }
                    if (!IsCurrent(link)) return false;   // a newer Connect owns the transport; this attempt says nothing

                    // The dial token is one-use; the ack mints the next one so a reconnect carries its own credential.
                    string rotated = ack.GetString("token");
                    if (!string.IsNullOrEmpty(rotated)) link.Token = rotated;

                    link.Stream = stream;     // enables Send; KMH traffic now flows over the API, off RWT chat
                    link.LastFailReason = null;   // a real link clears the warn-once state so a later failure is visible
                    KmhLog.Success("KMH API: connected - KMH traffic now uses the API transport.");
                    // API ack is the handshake when chat's down; activate KMH on main from its v/build
                    Features.KmhFeatures.SetDisabled(ack.GetString("disabled"));   // null when omitted -> keep last-known-good
                    string ackCaps = ack.GetString("capabilities");
                    KmhCapabilities.Apply(ackCaps, present: ackCaps != null);
                    int ackV = ack.GetInt("v", KmhProtocol.CurrentVersion);
                    string ackBuild = ack.GetString("build") ?? "";
                    KmhMainThread.Post(() => { if (IsCurrent(link)) KmhHandshakeHandler.ActivateSession(ackV, ackBuild, KmhTransportStatus.ApiConnected); });

                    using (CancellationTokenSource beatCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        Task beat = Heartbeat(link, beatCts.Token);
                        try
                        {
                            while (!ct.IsCancellationRequested && IsCurrent(link))
                            {
                                KmhEnvelope env = await ReadFrameWithin(stream, IdleTimeoutMs, ct).ConfigureAwait(false);
                                if (env == null) break; // server closed or idle
                                // Coalesced: one line every 15s is thousands over a session.
                                if (env.Kind == KindPong) { KmhLog.Heartbeat(); continue; }
                                // Guarded so the interpolated string is not built for every inbound message.
                                if (KmhLog.DebugEnabled) KmhLog.Protocol($"KMH API <= {env.Kind}");
                                KmhMainThread.Post(() => { if (IsCurrent(link)) KmhDispatcher.Receive(env); });   // handlers run on main
                            }
                        }
                        finally { link.Stream = null; beatCts.Cancel(); try { await beat.ConfigureAwait(false); } catch { } }
                    }
                }
            }
            return true;   // handshake succeeded earlier; the session has now ended, so a quick retry is fine
        }

        private static async Task Heartbeat(Link link, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(HeartbeatInterval, ct).ConfigureAwait(false); } catch { break; }
                if (ct.IsCancellationRequested || !IsCurrent(link)) break;
                if (SendOn(link, new KmhEnvelope(KindPing, null)) != KmhSendResult.Sent) break; // write failed -> connection dead
            }
        }

        public static bool TrySend(KmhEnvelope env) => Send(env) == KmhSendResult.Sent;

        // NotConnected is the only answer that proves the server has not seen this request.
        public static KmhSendResult Send(KmhEnvelope env) => SendOn(_current, env);

        private static KmhSendResult SendOn(Link link, KmhEnvelope env)
        {
            if (link == null || env == null) return KmhSendResult.NotConnected;
            NetworkStream s = link.Stream;
            if (s == null) return KmhSendResult.NotConnected;

            string wire;
            try { wire = env.Serialize(); }
            catch (Exception ex)
            {
                KmhLog.Warn($"KMH API: could not serialize '{env.Kind}': {ex.Message}");
                return KmhSendResult.SerializationFailure;
            }

            byte[] body = Encoding.UTF8.GetBytes(wire);
            if (body.Length > MaxFrameBytes)
            {
                // Fragmented rather than refused: the server reassembles before dispatch.
                KmhLog.Debug($"KMH API oversized outgoing envelope: kind={env.Kind} serialized={body.Length} bytes frameLimit={MaxFrameBytes} - fragmenting.");
                System.Collections.Generic.List<KmhEnvelope> parts = KmhFragments.Split(env.Kind, wire, MaxFrameBytes);
                if (parts == null) return KmhSendResult.TooLarge;
                for (int i = 0; i < parts.Count; i++)
                {
                    KmhSendResult r = SendOn(link, parts[i]);
                    if (r == KmhSendResult.Sent) continue;
                    // Parts already on the wire cannot be recalled, so past the first one resending elsewhere could submit the operation twice.
                    return i == 0 ? r : KmhSendResult.AmbiguousIoFailure;
                }
                return KmhSendResult.Sent;
            }

            try
            {
                byte[] frame = FrameOf(body);
                lock (link.SendLock) { s.Write(frame, 0, frame.Length); }
                return KmhSendResult.Sent;
            }
            catch (Exception ex)
            {
                KmhLog.Protocol($"KMH API: send '{env.Kind}' failed: {ex.Message}");
                return KmhSendResult.AmbiguousIoFailure;
            }
        }

        private static async Task<KmhEnvelope> ReadFrame(NetworkStream stream, CancellationToken ct)
        {
            byte[] lenBuf = await ReadExactly(stream, 4, ct).ConfigureAwait(false);
            if (lenBuf == null) return null;
            int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
            if (len <= 0 || len > MaxFrameBytes)
            {
                // Said out loud: this ends the receive loop, and silence here looks exactly like a dead feature.
                KmhLog.Warn($"KMH API rejected incoming frame: {len} bytes > {MaxFrameBytes} byte client limit");
                return null;
            }
            byte[] body = await ReadExactly(stream, len, ct).ConfigureAwait(false);
            if (body == null) return null;
            return KmhEnvelope.TryParse(Encoding.UTF8.GetString(body));
        }

        // ReadFrame but give up after timeoutMs of silence (async reads ignore NetworkStream.ReadTimeout)
        private static async Task<KmhEnvelope> ReadFrameWithin(NetworkStream stream, int timeoutMs, CancellationToken ct)
        {
            Task<KmhEnvelope> read = ReadFrame(stream, ct);
            if (await Task.WhenAny(read, Task.Delay(timeoutMs, ct)).ConfigureAwait(false) != read) return null;
            return await read.ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadExactly(NetworkStream stream, int count, CancellationToken ct)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n;
                try { n = await stream.ReadAsync(buf, read, count - read, ct).ConfigureAwait(false); }
                catch { return null; } // closed/disposed (incl. an abandoned read after an idle timeout)
                if (n <= 0) return null;
                read += n;
            }
            return buf;
        }

        // Hello only, before the stream is published, so there is a single writer and async is safe.
        private static async Task WriteFrame(NetworkStream stream, string kind, object data, CancellationToken ct)
        {
            byte[] frame = FrameOf(Encoding.UTF8.GetBytes(new KmhEnvelope(kind, data).Serialize()));
            await stream.WriteAsync(frame, 0, frame.Length, ct).ConfigureAwait(false);
        }

        // Symmetric with KmhApiServer's framing, so a change here has to be made there too.
        private static byte[] FrameOf(byte[] body)
        {
            byte[] frame = new byte[4 + body.Length];
            frame[0] = (byte)(body.Length >> 24); frame[1] = (byte)(body.Length >> 16);
            frame[2] = (byte)(body.Length >> 8);  frame[3] = (byte)body.Length;
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);
            return frame;
        }
    }
}
