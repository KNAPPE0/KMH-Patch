using System;
using System.Collections.Generic;
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

    public static class KmhApiClient
    {
        private const string KindApiHello    = "kmh.api.hello";
        private const string KindApiHelloAck = "kmh.api.hello.ack";
        private const string KindPing        = "kmh.ping";
        private const string KindPong        = "kmh.pong";

        private const int MaxFrameBytes      = 64 * 1024;
        // Advertised in the hello; capped at the frame size, a compressed catalog unpacking past it drops the link.
        internal const int MaxInflateBytes   = (int)KmhFragments.MaxTransferBytes;
        private const int ConnectTimeoutMs   = 5_000;
        private const int HelloAckTimeoutMs  = 10_000;   // a peer that accepts TCP and then says nothing
        private const int IdleTimeoutMs      = 40_000;   // drop + reconnect if the server goes silent past a heartbeat
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

        // One per Connect, sharing nothing, so an older attempt still unwinding cannot publish into its replacement.
        private sealed class Link
        {
            public int    Generation;
            public string Host;
            public string RwtHost;           // null when the player chose the host; else the RWT address the server was reached on
            public int    Port;
            public string User;
            public string Token;             // rotated by each successful ack, so a reconnect has its own credential
            public bool   AllowChatFallback;
            public bool   PeerDeflate;       // negotiated per connection; a reconnect re-asks rather than assuming
            public int    PeerFrameBytes   = MaxFrameBytes;   // the server's word at hello, never more than ours
            public int    PeerInflateBytes = MaxFrameBytes;   // an older server unpacks no more than its frame size
            public CancellationTokenSource Cts;
            public TcpClient     Socket;
            public NetworkStream Stream;     // non-null only while this link's API is up
            public KmhSendQueue  Outbox;     // the only writer once the link is up
            public string LastFailReason;
            public readonly KmhFragments.Assembler Fragments = new KmhFragments.Assembler();   // read loop only
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

        internal static string LinkSummary
        {
            get
            {
                Link l = _current;
                if (l == null || l.Stream == null) return "not connected";
                return $"frames up to {l.PeerFrameBytes / 1024} KB, the server unpacks up to {l.PeerInflateBytes / 1024} KB, "
                     + $"compression {(l.PeerDeflate ? "on" : "off")}, {(l.Outbox?.QueuedBytes ?? 0) / 1024} KB waiting to send";
            }
        }

        // Only the live attempt may publish status, activate a session, or claim the transport.
        private static bool IsCurrent(Link l) => l != null && ReferenceEquals(_current, l) && !l.Cts.IsCancellationRequested;

        // The early dial may have used the local setting; the server's chat-fallback policy wins.
        public static void SetChatFallbackAllowed(bool allowed)
        {
            Link l = _current;
            if (l != null) l.AllowChatFallback = allowed;
        }

        public static bool ChatFallbackAllowed
        {
            get { Link l = _current; return l == null || l.AllowChatFallback; }
        }

        public static void Connect(string host, int port, string username, string token, bool allowChatFallback, string rwtHost = null)
        {
            Disconnect();
            if (string.IsNullOrEmpty(host) || port <= 0) { KmhLog.Warn("KMH API: no host/port - staying on chat."); return; }
            Link link = new Link
            {
                Generation = Interlocked.Increment(ref _generation),
                Host = host, Port = port, RwtHost = rwtHost,
                User = username ?? "", Token = token ?? "",
                AllowChatFallback = allowChatFallback,
                Cts = new CancellationTokenSource(),
            };
            _current = link;
            KmhMainThread.OnOverload = ResetForOverload;
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
            try { l.Outbox?.Close(); } catch { }
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
                        KmhTransport.Status = KmhTransportStatus.ChatFallback;
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

            // Disposed by its own loop, or every reconnect abandons a source still holding its registrations.
            // Safe: the token was captured above, and IsCancellationRequested keeps answering after a dispose.
            try { link.Cts.Dispose(); } catch { }
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
            // Resolved here, off the main thread: a server reached over the internet may not aim the client into a private network.
            if (link.RwtHost != null && !KmhDialPolicy.MayDial(link.RwtHost, link.Host, out string refused))
            {
                KmhLog.Warn($"KMH API: not dialing the advertised {link.Host} ({refused}); using {link.RwtHost} instead.");
                link.Host = link.RwtHost;
                link.RwtHost = null;
            }
            using (TcpClient client = new TcpClient())
            {
                link.Socket = client;
                Task connect = client.ConnectAsync(link.Host, link.Port);
                if (!await FinishesWithin(connect, ConnectTimeoutMs, ct).ConfigureAwait(false))
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
                        max_frame_kb = MaxFrameBytes / 1024, supports_fragmentation = true, supports_deflate = true,
                        max_inflate_kb = MaxInflateBytes / 1024,
                    }, ct).ConfigureAwait(false);

                    // Bounded: a peer that connects and goes quiet would hold this attempt open and stall the backoff ladder.
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

                    // Off unless the server said so on this connection: an older server drops the link on a deflated frame.
                    link.PeerDeflate = ack.GetBool("supports_deflate", false);
                    // A server with a smaller MaxFrameKb refuses larger frames and drops the link.
                    NegotiateLimits(link, ack.GetInt("max_frame_kb", 0), ack.GetInt("max_inflate_kb", 0));
                    link.Outbox = new KmhSendQueue(stream, link.PeerDeflate, link.PeerFrameBytes, link.PeerInflateBytes,
                        why =>
                        {
                            NoteFailure(link, "send: " + why, $"KMH API: sending to {link.Host}:{link.Port} failed ({why}) - reconnecting.");
                            Invalidate(link);
                        });
                    KmhClientTransportStats.Connected();

                    link.Stream = stream;
                    link.LastFailReason = null;   // a real link clears the warn-once state so a later failure is visible
                    KmhLog.Success("KMH API: connected - KMH traffic now uses the API transport.");
                    // The API ack is the handshake when chat is down.
                    string ackDisabled = ack.GetString("disabled");   // null when omitted -> keep last-known-good
                    string ackCaps = ack.GetString("capabilities");
                    int ackV = ack.GetInt("v", KmhProtocol.CurrentVersion);
                    string ackBuild = ack.GetString("build") ?? "";
                    // The UI reads both sets every frame, so they change on the main thread, as the chat hello's do.
                    KmhMainThread.Post(() =>
                    {
                        if (!IsCurrent(link)) return;
                        Features.KmhFeatures.SetDisabled(ackDisabled);
                        KmhCapabilities.Apply(ackCaps, present: ackCaps != null);
                        KmhHandshakeHandler.ActivateSession(ackV, ackBuild, KmhTransportStatus.ApiConnected);
                    });

                    using (CancellationTokenSource beatCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        Task beat = Heartbeat(link, beatCts.Token);
                        try
                        {
                            while (!ct.IsCancellationRequested && IsCurrent(link))
                            {
                                KmhEnvelope env = await ReadFrameWithin(stream, IdleTimeoutMs, ct).ConfigureAwait(false);
                                if (env == null) break; // server closed or idle
                                KmhEnvelope whole = ForGameThread(link.Fragments, env);
                                if (whole == null) continue;
                                // Guarded so the interpolated string is not built for every inbound message.
                                if (KmhLog.DebugEnabled) KmhLog.Protocol($"KMH API <= {whole.Kind}");
                                // Handlers run on main; a snapshot already outdated in the queue is skipped there.
                                string latest = LatestKey(link.Generation, whole);
                                if (latest == null) KmhMainThread.Post(() => { if (IsCurrent(link)) KmhDispatcher.Receive(whole); });
                                else KmhMainThread.PostLatest(latest, whole.GetLong("revision", -1), () => { if (IsCurrent(link)) KmhDispatcher.Receive(whole); });
                            }
                        }
                        finally
                        {
                            link.Stream = null;
                            link.Outbox?.Close();
                            beatCts.Cancel();
                            try { await beat.ConfigureAwait(false); } catch { }
                        }
                    }
                }
            }
            return true;   // handshake succeeded earlier; the session has now ended, so a quick retry is fine
        }

        // Whole-cache snapshots only. Not Enforcement (a state machine), Chat (its first snapshot marks history read),
        // Reputation or GuildInvitables (a null list keeps the old one, so an older snapshot can matter).
        private static readonly HashSet<string> LatestWins = new HashSet<string>(StringComparer.Ordinal)
        {
            KmhProtocol.Kind.PlayerStatsSnapshot, KmhProtocol.Kind.TreasurySnapshot, KmhProtocol.Kind.MarketplaceSnapshot,
            KmhProtocol.Kind.QuestSnapshot, KmhProtocol.Kind.GuildSnapshot, KmhProtocol.Kind.GuildLeaderboardSnapshot,
            KmhProtocol.Kind.LinkedAccountsSnapshot, KmhProtocol.Kind.WorldSnapshot, KmhProtocol.Kind.AuctionSnapshot,
            KmhProtocol.Kind.WantSnapshot, KmhProtocol.Kind.MailSnapshot, KmhProtocol.Kind.ConnectedSnapshot,
            KmhProtocol.Kind.GuildConnectedSnapshot, KmhProtocol.Kind.FederationSnapshot, KmhProtocol.Kind.SiteSnapshot,
            KmhProtocol.Kind.RoadworksSnapshot, KmhProtocol.Kind.LeaderboardsSnapshot,
        };

        // Scoped to one link: the chat path and a later link can deliver out of order, which only the revision guards settle.
        // Treasury carries the personal and guild vaults under one kind, each with its own revision.
        internal static string LatestKey(int generation, KmhEnvelope env)
        {
            if (env?.Kind == null || !LatestWins.Contains(env.Kind)) return null;
            string key = generation + "|" + env.Kind;
            return env.Kind == KmhProtocol.Kind.TreasurySnapshot ? key + "|" + (env.GetString("owner_key") ?? "") : key;
        }

        // A new link makes every message queued from the old one fall through IsCurrent, and the join re-sends every snapshot.
        private static void ResetForOverload()
        {
            Link l = _current;
            if (l != null) Connect(l.Host, l.Port, l.User, l.Token, l.AllowChatFallback);
        }

        // Joined on the link's thread, so decoding a large transfer costs the game no frame.
        internal static KmhEnvelope ForGameThread(KmhFragments.Assembler fragments, KmhEnvelope env)
        {
            if (env == null) return null;
            // Coalesced: one line every 15s is thousands over a session.
            if (env.Kind == KindPong) { KmhLog.Heartbeat(); return null; }
            if (!KmhFragments.IsFragment(env.Kind)) return env;
            KmhEnvelope whole = fragments.Accept(env);
            if (whole != null) KmhClientTransportStats.AssembledOffMain();
            return whole;
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
            KmhSendQueue outbox = link.Outbox;
            if (link.Stream == null || outbox == null || outbox.IsDead) return KmhSendResult.NotConnected;

            string wire;
            try { wire = env.Serialize(); }
            catch (Exception ex)
            {
                KmhLog.Warn($"KMH API: could not serialize '{env.Kind}': {ex.Message}");
                return KmhSendResult.SerializationFailure;
            }

            byte[] body = Encoding.UTF8.GetBytes(wire);

            // Decided before queueing, so the caller still gets TooLarge.
            if (body.Length > link.PeerFrameBytes && !KmhFragments.CanSplit(body.Length, link.PeerFrameBytes))
            {
                KmhLog.Warn($"Transport: '{env.Kind}' is {body.Length} bytes, past what the link can carry - not sent.");
                return KmhSendResult.TooLarge;
            }

            // Sent still means handed to the transport; a write that fails later ends the link.
            return outbox.Enqueue(env.Kind, body) ? KmhSendResult.Sent : KmhSendResult.NotConnected;
        }

        // An older server sends neither: 64 KB frames, and nothing unpacked past them.
        private static void NegotiateLimits(Link link, int frameKb, int inflateKb)
        {
            link.PeerFrameBytes = frameKb > 0 ? Math.Min(MaxFrameBytes, frameKb * 1024) : MaxFrameBytes;
            link.PeerInflateBytes = inflateKb > 0
                ? (int)Math.Max(link.PeerFrameBytes, Math.Min((long)inflateKb * 1024, KmhFragments.MaxTransferBytes))
                : link.PeerFrameBytes;
        }

        internal static (int Frame, int Inflate) LimitsForTest(int frameKb, int inflateKb)
        {
            Link l = new Link();
            NegotiateLimits(l, frameKb, inflateKb);
            return (l.PeerFrameBytes, l.PeerInflateBytes);
        }

        private static async Task<KmhEnvelope> ReadFrame(NetworkStream stream, CancellationToken ct)
        {
            byte[] lenBuf = await ReadExactly(stream, 4, ct).ConfigureAwait(false);
            if (lenBuf == null) return null;
            KmhFrameCodec.ReadHeader(lenBuf, out int len, out bool compressed);
            if (len <= 0 || len > MaxFrameBytes)
            {
                // Said out loud: this ends the receive loop, and silence here looks exactly like a dead feature.
                KmhLog.Warn($"KMH API rejected incoming frame: {len} bytes > {MaxFrameBytes} byte client limit");
                return null;
            }
            byte[] body = await ReadExactly(stream, len, ct).ConfigureAwait(false);
            if (body == null) return null;
            KmhClientTransportStats.FrameIn(4 + len);
            if (compressed)
            {
                // Our advertised cap, so a few KB cannot inflate without limit.
                body = KmhFrameCodec.Inflate(body, MaxInflateBytes);
                if (body == null)
                {
                    KmhLog.Warn($"KMH API rejected incoming frame: it did not decompress within {MaxInflateBytes / 1024} KB");
                    return null;
                }
                KmhClientTransportStats.Inflated();
            }
            return KmhEnvelope.TryParse(Encoding.UTF8.GetString(body));
        }

        internal static Task<KmhEnvelope> ReadFrameForTest(NetworkStream stream, CancellationToken ct) => ReadFrame(stream, ct);

        // Async reads ignore NetworkStream.ReadTimeout, so silence is bounded here.
        private static async Task<KmhEnvelope> ReadFrameWithin(NetworkStream stream, int timeoutMs, CancellationToken ct)
        {
            Task<KmhEnvelope> read = ReadFrame(stream, ct);
            if (!await FinishesWithin(read, timeoutMs, ct).ConfigureAwait(false)) return null;
            return await read.ConfigureAwait(false);
        }

        // The timer is cancelled with the wait; left running, every read parked one for the whole idle window.
        internal static async Task<bool> FinishesWithin(Task task, int timeoutMs, CancellationToken ct)
        {
            if (task.IsCompleted) return true;
            using (CancellationTokenSource delay = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                Task first = await Task.WhenAny(task, Task.Delay(timeoutMs, delay.Token)).ConfigureAwait(false);
                delay.Cancel();
                return first == task;
            }
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
        private static byte[] FrameOf(byte[] body) => KmhFrameCodec.Frame(body, false);
    }
}
