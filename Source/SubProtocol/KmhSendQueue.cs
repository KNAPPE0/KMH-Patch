using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;

namespace KMHPatch.SubProtocol
{
    // One writer per link, off the game's thread: a 1 MB send on a slow uplink would hold the game for seconds.
    internal sealed class KmhSendQueue
    {
        // A server that has left this much unread is not coming back; reconnect rather than buffer more.
        internal const long MaxQueuedBytes = 16L * 1024 * 1024;
        internal const int CoalesceBytes = 64 * 1024;
        internal static int WriteTimeoutMs = 30_000;

        private readonly Stream _stream;
        private readonly Action<string> _onFail;
        private readonly bool _deflate;
        private readonly int _frameBytes, _inflateBytes;
        private readonly ConcurrentQueue<Message> _queue = new ConcurrentQueue<Message>();
        private long _queued;
        private int _draining;
        private int _dead;
        private byte[] _batch;

        private sealed class Message
        {
            public string Kind;
            public byte[] Body;
        }

        public KmhSendQueue(Stream stream, bool deflate, int frameBytes, int inflateBytes, Action<string> onFail)
        {
            _stream = stream;
            _deflate = deflate;
            _frameBytes = Math.Max(1024, frameBytes);
            _inflateBytes = Math.Max(_frameBytes, inflateBytes);
            _onFail = onFail;
        }

        public bool IsDead => Volatile.Read(ref _dead) != 0;
        public long QueuedBytes => Interlocked.Read(ref _queued);

        // False: nothing of this message was queued.
        public bool Enqueue(string kind, byte[] body)
        {
            if (body == null || IsDead) return false;
            long now = Interlocked.Add(ref _queued, body.Length);
            if (now > MaxQueuedBytes)
            {
                Interlocked.Add(ref _queued, -body.Length);
                Fail($"the server has left {now / 1024} KB of KMH traffic unread");
                return false;
            }
            KmhClientTransportStats.QueueDepth(now);
            _queue.Enqueue(new Message { Kind = kind ?? "", Body = body });
            if (Interlocked.CompareExchange(ref _draining, 1, 0) == 0) Task.Run(DrainAsync);
            return true;
        }

        // The re-check after standing down covers a message queued at that moment.
        private async Task DrainAsync()
        {
            try
            {
                while (true)
                {
                    if (IsDead) { Drop(); return; }
                    if (!_queue.TryDequeue(out Message m))
                    {
                        Volatile.Write(ref _draining, 0);
                        if (_queue.IsEmpty || Interlocked.CompareExchange(ref _draining, 1, 0) != 0) return;
                        continue;
                    }

                    List<byte[]> frames = FramesFor(m, out int compressed, out long raw);
                    long accounted = m.Body.Length;
                    if (frames == null) { Interlocked.Add(ref _queued, -accounted); continue; }

                    int length = 0;
                    foreach (byte[] f in frames) length += f.Length;
                    int written = frames.Count, writes = 1;
                    if (length > CoalesceBytes)
                    {
                        foreach (byte[] f in frames) await WriteAsync(f, f.Length).ConfigureAwait(false);
                        writes = frames.Count;
                    }
                    else
                    {
                        // Only this writer dequeues, so a peeked message is still next; small ones are never packed or cut.
                        byte[] buffer = _batch = _batch ?? new byte[CoalesceBytes];
                        int at = 0;
                        foreach (byte[] f in frames) { Buffer.BlockCopy(f, 0, buffer, at, f.Length); at += f.Length; }
                        while (_queue.TryPeek(out Message next) && next.Body.Length < KmhFrameCodec.CompressAboveBytes
                               && next.Body.Length <= _frameBytes && at + 4 + next.Body.Length <= buffer.Length
                               && _queue.TryDequeue(out next))
                        {
                            byte[] f = KmhFrameCodec.Frame(next.Body, false);
                            Buffer.BlockCopy(f, 0, buffer, at, f.Length);
                            at += f.Length;
                            accounted += next.Body.Length;
                            raw += f.Length;
                            written++;
                        }
                        length = at;
                        await WriteAsync(buffer, length).ConfigureAwait(false);
                    }

                    Interlocked.Add(ref _queued, -accounted);
                    KmhClientTransportStats.Written(written, length, raw, compressed, writes);
                }
            }
            catch (Exception ex)
            {
                // TCP has no transient write failure: this link is finished.
                KmhClientTransportStats.WriteFailed();
                Volatile.Write(ref _draining, 0);
                Fail(ex.Message);
            }
        }

        // Bounded, so a receive window that stays shut ends the link.
        private async Task WriteAsync(byte[] buffer, int length)
        {
            Task write = _stream.WriteAsync(buffer, 0, length);
            if (!await KmhApiClient.FinishesWithin(write, WriteTimeoutMs, CancellationToken.None).ConfigureAwait(false))
            {
                // Observed, or closing the socket under it raises an unobserved-task fault.
                _ = write.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException($"a write did not finish within {WriteTimeoutMs / 1000}s");
            }
            await write.ConfigureAwait(false);
        }

        // Packed only up to what the server unpacks: an older one unpacks no more than its frame and refuses the rest.
        private List<byte[]> FramesFor(Message m, out int compressed, out long raw)
        {
            compressed = 0;
            raw = 4L + m.Body.Length;
            byte[] packed = _deflate && m.Body.Length <= _inflateBytes ? KmhFrameCodec.Deflate(m.Body) : null;
            if (packed != null && packed.Length <= _frameBytes)
            {
                compressed = 1;
                return new List<byte[]> { KmhFrameCodec.Frame(packed, true) };
            }
            if (m.Body.Length <= _frameBytes) return new List<byte[]> { KmhFrameCodec.Frame(m.Body, false) };

            List<KmhEnvelope> parts = KmhFragments.Split(m.Kind, m.Body, _frameBytes);
            if (parts == null) return null;   // Split has said why
            var frames = new List<byte[]>(parts.Count);
            raw = 0;
            foreach (KmhEnvelope part in parts)
            {
                byte[] body = Encoding.UTF8.GetBytes(part.Serialize());
                raw += 4L + body.Length;
                byte[] z = _deflate ? KmhFrameCodec.Deflate(body) : null;
                if (z != null && z.Length <= _frameBytes) { frames.Add(KmhFrameCodec.Frame(z, true)); compressed++; }
                else if (body.Length <= _frameBytes) frames.Add(KmhFrameCodec.Frame(body, false));
                else
                {
                    KmhLog.Warn($"KMH API: a fragment of '{m.Kind}' came out larger than the {_frameBytes}-byte frame - not sent.");
                    return null;
                }
            }
            KmhLog.Debug($"KMH API: '{m.Kind}' ({m.Body.Length} bytes) went as {frames.Count} fragment(s) of at most {_frameBytes} bytes.");
            return frames;
        }

        private void Fail(string reason)
        {
            if (Interlocked.Exchange(ref _dead, 1) != 0) return;
            Drop();
            try { _onFail?.Invoke(reason); } catch { }
        }

        private void Drop()
        {
            while (_queue.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _queued, 0);
        }

        // Nothing still waiting may reach whatever link replaces this one.
        public void Close()
        {
            Interlocked.Exchange(ref _dead, 1);
            Drop();
        }

        internal bool WaitDrained(TimeSpan max)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!IsDead && (Interlocked.Read(ref _queued) > 0 || Volatile.Read(ref _draining) != 0))
            {
                if (sw.Elapsed >= max) return false;
                Thread.Sleep(5);
            }
            return !IsDead;
        }
    }
}
