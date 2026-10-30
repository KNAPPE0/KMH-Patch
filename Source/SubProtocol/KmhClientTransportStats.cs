using System.Collections.Generic;
using System.Threading;

namespace KMHPatch.SubProtocol
{
    // Interlocked from the socket threads; readers take a snapshot and never hold them up.
    internal static class KmhClientTransportStats
    {
        private static long _framesIn, _bytesIn, _inflatedFrames, _framesOut, _bytesOut, _rawOut, _compressedOut;
        private static long _writes, _queuePeak, _writeFailures, _assembledOffMain, _backlogPeak, _connects;

        internal static void FrameIn(long wireBytes) { Interlocked.Increment(ref _framesIn); Interlocked.Add(ref _bytesIn, wireBytes); }
        internal static void Inflated() => Interlocked.Increment(ref _inflatedFrames);
        internal static void AssembledOffMain() => Interlocked.Increment(ref _assembledOffMain);
        internal static void Connected() => Interlocked.Increment(ref _connects);
        internal static void WriteFailed() => Interlocked.Increment(ref _writeFailures);

        internal static void Written(int frames, long wireBytes, long rawBytes, int compressedFrames, int writes)
        {
            Interlocked.Add(ref _writes, writes);
            Interlocked.Add(ref _framesOut, frames);
            Interlocked.Add(ref _bytesOut, wireBytes);
            Interlocked.Add(ref _rawOut, rawBytes);
            Interlocked.Add(ref _compressedOut, compressedFrames);
        }

        internal static void QueueDepth(long bytes) => Raise(ref _queuePeak, bytes);
        internal static void Backlog(long actions) => Raise(ref _backlogPeak, actions);

        private static void Raise(ref long peak, long value)
        {
            long seen;
            while (value > (seen = Interlocked.Read(ref peak)))
                if (Interlocked.CompareExchange(ref peak, value, seen) == seen) return;
        }

        internal static long BytesIn => Interlocked.Read(ref _bytesIn);
        internal static long BytesOut => Interlocked.Read(ref _bytesOut);

        internal static List<KeyValuePair<string, long>> Snapshot()
        {
            List<KeyValuePair<string, long>> all = Transport();
            all.AddRange(Diagnostics.KmhMainThread.Stats());
            return all;
        }

        private static List<KeyValuePair<string, long>> Transport() => new List<KeyValuePair<string, long>>
        {
            Pair("frames_in", ref _framesIn),
            Pair("bytes_in", ref _bytesIn),
            Pair("compressed_frames_in", ref _inflatedFrames),
            Pair("transfers_assembled_off_main_thread", ref _assembledOffMain),
            Pair("frames_out", ref _framesOut),
            Pair("bytes_out", ref _bytesOut),
            Pair("bytes_out_uncompressed", ref _rawOut),
            Pair("compressed_frames_out", ref _compressedOut),
            Pair("socket_writes", ref _writes),
            Pair("send_queue_peak_bytes", ref _queuePeak),
            Pair("write_failures", ref _writeFailures),
            Pair("main_thread_backlog_peak", ref _backlogPeak),
            Pair("connections", ref _connects),
        };

        private static KeyValuePair<string, long> Pair(string name, ref long value)
            => new KeyValuePair<string, long>(name, Interlocked.Read(ref value));

        internal static long Get(string name)
        {
            foreach (KeyValuePair<string, long> kv in Snapshot()) if (kv.Key == name) return kv.Value;
            return -1;
        }
    }
}
