using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Global
{
    internal static class KmhDirectoryStream
    {
        public static bool Connected { get; private set; }
        public static long Revision { get; private set; }
        public static DateTime LastEventUtc { get; private set; }

        internal static Action Resync = () => _ = KmhDirectoryClient.RefreshNowAsync(TimeSpan.FromSeconds(12));

        private static CancellationTokenSource _cts;
        private static readonly object _gate = new object();

        // A card is ~1 KB and the fleet never arrives in one frame, so anything larger is not a directory event.
        private const int MaxFrameBytes = 256 * 1024;

        public static void Start()
        {
            CancellationTokenSource mine;
            lock (_gate)
            {
                if (_cts != null) return;
                _cts = mine = new CancellationTokenSource();
            }
            // Disposed by the owning loop, not Stop: disposing while the loop holds its token throws on the next read.
            Task.Run(async () =>
            {
                try { await RunAsync(mine.Token).ConfigureAwait(false); }
                finally { mine.Dispose(); }
            });
        }

        public static void Stop()
        {
            CancellationTokenSource cts;
            lock (_gate) { cts = _cts; _cts = null; }
            if (cts == null) return;
            try { cts.Cancel(); } catch (Exception) { }
            Connected = false;
        }

        // The first event after connect is the baseline; repeats and late ones change nothing; a forward jump re-reads.
        internal static bool IsGap(long current, long incoming) => current != 0 && incoming > current + 1;

        public static void Reset()
        {
            Stop();
            Revision = 0;
            LastEventUtc = default;
        }

        private static async Task RunAsync(CancellationToken ct)
        {
            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Only a socket that carried data resets backoff: one refused at the door is refused again in 2s.
                    if (await StreamAsync(ct).ConfigureAwait(false)) attempt = 0;
                }
                catch (OperationCanceledException) { return; }
                catch (NotSupportedException ex) { KmhLog.Info($"Directory stream: not opened ({ex.Message}); the list refreshes by polling instead."); return; }
                catch (Exception ex) { KmhLog.Debug($"Directory stream: {ex.GetType().Name}; reconnecting."); }

                Connected = false;
                if (ct.IsCancellationRequested) return;
                try { await Task.Delay(BackoffFor(attempt++), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        internal static TimeSpan BackoffFor(int attempt) => KmhRealtimeBackoff.For(attempt);

        private static async Task<bool> StreamAsync(CancellationToken ct)
        {
            // Taken with the address: every event on this socket belongs to the directory it was opened to.
            int generation = KmhDirectoryClient.Generation;
            string url = GlobalHttp.BaseUrl.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://")
                       + "/api/v1/directory/stream";

            using (ClientWebSocket socket = NewSocket(url))
            {
                if (socket == null) throw new NotSupportedException("this runtime cannot check the directory's certificate per connection");
                await socket.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);
                Connected = true;

                bool carried = false;
                byte[] buffer = new byte[16 * 1024];
                using (MemoryStream frame = new MemoryStream())
                {
                    while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        WebSocketReceiveResult got = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                        if (got.MessageType == WebSocketMessageType.Close)
                        {
                            // Refused rather than dropped, so the backoff is left to escalate.
                            if (socket.CloseStatus == WebSocketCloseStatus.PolicyViolation) return false;
                            break;
                        }
                        if (got.Count <= 0 && !got.EndOfMessage) continue;

                        // A card may span several receives; parsing a partial frame dropped events larger than the buffer.
                        if (frame.Length + got.Count > MaxFrameBytes)
                        {
                            KmhLog.Debug("Directory stream: a frame exceeded the size cap; reconnecting.");
                            return carried;
                        }
                        frame.Write(buffer, 0, got.Count);
                        if (!got.EndOfMessage) continue;

                        carried = true;
                        string text = Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length);
                        frame.SetLength(0);
                        ApplyFrom(text, generation);
                    }
                }
                return carried;
            }
        }

        // Cards on this socket can be marked Official, so it checks certificates itself, as the polled directory does;
        // a process-wide setting another mod can switch off is not enough, and without the check the socket stays closed.
        internal static ClientWebSocket NewSocket(string url)
        {
            var socket = new ClientWebSocket();
            if (!url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) || KmhHttps.CheckSocket(socket.Options)) return socket;
            socket.Dispose();
            return null;
        }

        private static void Apply(string text) => ApplyFrom(text, KmhDirectoryClient.Generation);

        // A malformed frame costs one event, never the connection.
        internal static void ApplyFrom(string text, int generation)
        {
            if (generation != KmhDirectoryClient.Generation) return;
            JObject evt;
            try { evt = JObject.Parse(text); }
            catch (Exception) { return; }

            long revision = (long?)evt["revision"] ?? 0;
            string type = (string)evt["type"] ?? "";

            // A gap means a missed event: re-read rather than half-apply, and take this revision as the new baseline.
            bool gap = IsGap(Revision, revision);
            Revision = revision;
            LastEventUtc = DateTime.UtcNow;

            if (type == "hello" || gap)
            {
                KmhDirectoryClient.ApplyPushedCounters((int?)evt["server_count"] ?? 0, (int?)evt["player_count"] ?? 0, generation);
                Resync();
                return;
            }

            if (type == "counters")
            {
                KmhDirectoryClient.ApplyPushedCounters((int?)evt["server_count"] ?? 0, (int?)evt["player_count"] ?? 0, generation);
                return;
            }

            if (type == "server")
            {
                ServerCardDto card = DirectoryParse.Card(evt["server"] as JObject);
                if (card != null) KmhDirectoryClient.ApplyPushedCard(card, generation);
            }
        }
    }
}
