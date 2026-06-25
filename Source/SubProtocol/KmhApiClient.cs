using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;

namespace KMHPatch.SubProtocol
{
    // Client-side KMH API transport; uses direct TCP when enabled/advertised, otherwise falls back to chat.
    public static class KmhApiClient
    {
        private const string KindApiHello    = "kmh.api.hello";
        private const string KindApiHelloAck = "kmh.api.hello.ack";
        private const string KindPing        = "kmh.ping";
        private const string KindPong        = "kmh.pong";

        private const int MaxFrameBytes      = 64 * 1024;
        private const int ConnectTimeoutMs   = 5_000;
        private const int IdleTimeoutMs      = 40_000;   // drop + reconnect if the server goes silent past a heartbeat
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

        private static CancellationTokenSource _cts;
        private static string _host, _token, _username;
        private static int _port;
        private static bool _allowChatFallback = true;
        private static bool _failAnnounced;
        private static NetworkStream _activeStream;          // non-null only while the API link is up
        private static readonly object _sendLock = new object();

        public static bool Active => _cts != null && !_cts.IsCancellationRequested;

        // start/restart the link; no-ops without host/port
        public static void Connect(string host, int port, string username, string token, bool allowChatFallback)
        {
            Disconnect();
            if (string.IsNullOrEmpty(host) || port <= 0) { KmhLog.Warn("KMH API: no host/port - staying on chat."); return; }
            _host = host; _port = port; _username = username ?? ""; _token = token ?? ""; _allowChatFallback = allowChatFallback;
            _failAnnounced = false;
            _cts = new CancellationTokenSource();
            KmhTransport.Status = KmhTransportStatus.ApiConnecting;
            KmhLog.Info($"KMH API: transport on - dialing {host}:{port} as {(string.IsNullOrEmpty(_username) ? "?" : _username)}.");
            Task.Run(() => RunLoop(_cts.Token));
        }

        public static void Disconnect()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
        }

        private static async Task RunLoop(CancellationToken ct)
        {
            int backoffMs = 1_000;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ServeOnce(ct).ConfigureAwait(false);
                    backoffMs = 1_000; // clean session ended (peer closed) - reset backoff
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!_failAnnounced)
                    {
                        _failAnnounced = true;   // first failure visible; later retries debug-only
                        KmhLog.Warn($"KMH API: {_host}:{_port} unreachable ({ex.Message}) - {(_allowChatFallback ? "staying on RWT chat" : "offline")}. Retrying in the background.");
                    }
                    else KmhLog.Protocol($"KMH API: link error: {ex.Message}");
                    if (_allowChatFallback && KmhTransport.Status != KmhTransportStatus.ApiConnected)
                        KmhTransport.Status = KmhTransportStatus.ChatFallback; // chat still serves features
                }
                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(backoffMs, ct).ConfigureAwait(false); } catch { break; }
                backoffMs = Math.Min(backoffMs * 2, 30_000);
            }
        }

        private static async Task ServeOnce(CancellationToken ct)
        {
            using (TcpClient client = new TcpClient())
            {
                Task connect = client.ConnectAsync(_host, _port);
                if (await Task.WhenAny(connect, Task.Delay(ConnectTimeoutMs, ct)).ConfigureAwait(false) != connect)
                    throw new TimeoutException($"connect to {_host}:{_port} timed out");
                await connect.ConfigureAwait(false); // surface connect errors
                client.NoDelay = true;

                using (NetworkStream stream = client.GetStream())
                {
                    KmhLog.Protocol($"KMH API: connected to {_host}:{_port}, sending hello.");
                    await WriteFrame(stream, KindApiHello, new { v = KmhProtocol.CurrentVersion, build = KmhProtocol.BuildVersion, username = _username, token = _token }, ct).ConfigureAwait(false);

                    KmhEnvelope ack = await ReadFrame(stream, ct).ConfigureAwait(false);
                    if (ack == null || ack.Kind != KindApiHelloAck || !ack.GetBool("ok"))
                    {
                        string reason = ack?.GetString("reason") ?? "no ack";
                        KmhTransport.Status = reason == "auth" ? KmhTransportStatus.AuthFailed
                            : (_allowChatFallback ? KmhTransportStatus.ChatFallback : KmhTransportStatus.Offline);
                        KmhLog.Warn($"KMH API: handshake rejected ({reason}) - {(_allowChatFallback ? "falling back to chat." : "offline.")}");
                        return; // don't hammer-retry an auth failure; loop backoff applies
                    }

                    _activeStream = stream;   // enables TrySend; KMH traffic now flows over the API, off RWT chat
                    KmhLog.Success("KMH API: connected - KMH traffic now uses the API transport.");
                    // API ack is the handshake when chat's down; activate KMH on main from its v/build
                    int ackV = ack.GetInt("v", KmhProtocol.CurrentVersion);
                    string ackBuild = ack.GetString("build") ?? "";
                    KmhMainThread.Post(() => KmhHandshakeHandler.ActivateSession(ackV, ackBuild, KmhTransportStatus.ApiConnected));

                    using (CancellationTokenSource link = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        Task beat = Heartbeat(link.Token);
                        try
                        {
                            while (!ct.IsCancellationRequested)
                            {
                                KmhEnvelope env = await ReadFrameWithin(stream, IdleTimeoutMs, ct).ConfigureAwait(false);
                                if (env == null) break; // server closed or idle
                                if (env.Kind == KindPong) { KmhLog.Protocol("KMH API: pong."); continue; }
                                KmhLog.Protocol($"KMH API <= {env.Kind}");
                                KmhMainThread.Post(() => KmhDispatcher.Receive(env));   // handlers run on main
                            }
                        }
                        finally { _activeStream = null; link.Cancel(); try { await beat.ConfigureAwait(false); } catch { } }
                    }
                }
            }
        }

        private static async Task Heartbeat(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(HeartbeatInterval, ct).ConfigureAwait(false); } catch { break; }
                if (ct.IsCancellationRequested) break;
                if (!TrySend(new KmhEnvelope(KindPing, null))) break; // write failed -> connection dead
            }
        }

        // serialized sync write over the live socket (heartbeat + KmhDispatcher.Send); false -> not connected, use chat
        public static bool TrySend(KmhEnvelope env)
        {
            NetworkStream s = _activeStream;
            if (s == null || env == null) return false;
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(env.Serialize());
                if (body.Length > MaxFrameBytes) return false;
                byte[] frame = FrameOf(body);
                lock (_sendLock) { s.Write(frame, 0, frame.Length); }
                return true;
            }
            catch { return false; }
        }

        // --- framing (symmetric with KmhApiServer) ---

        private static async Task<KmhEnvelope> ReadFrame(NetworkStream stream, CancellationToken ct)
        {
            byte[] lenBuf = await ReadExactly(stream, 4, ct).ConfigureAwait(false);
            if (lenBuf == null) return null;
            int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
            if (len <= 0 || len > MaxFrameBytes) return null;
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

        // hello only, before _activeStream is set - single writer, async is fine
        private static async Task WriteFrame(NetworkStream stream, string kind, object data, CancellationToken ct)
        {
            byte[] frame = FrameOf(Encoding.UTF8.GetBytes(new KmhEnvelope(kind, data).Serialize()));
            await stream.WriteAsync(frame, 0, frame.Length, ct).ConfigureAwait(false);
        }

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
