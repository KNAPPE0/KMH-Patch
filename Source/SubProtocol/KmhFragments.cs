using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using KMHPatch.Diagnostics;

namespace KMHPatch.SubProtocol
{
    // Mirror of the server's fragmentation layer; the two must stay byte-compatible.
    internal static class KmhFragments
    {
        public const string Kind = "kmh.fragment";

        public const int RawChunkBytes = 32 * 1024;
        public const int  MaxChunks              = 512;
        public const long MaxTransferBytes       = 8L * 1024 * 1024;
        public const int  MaxConcurrentTransfers = 4;
        public const int  AssemblyTimeoutSeconds = 30;

        public static bool IsFragment(string kind) => string.Equals(kind, Kind, StringComparison.Ordinal);

        public static List<KmhEnvelope> Split(string kind, string serialized, int safeFrameBytes)
        {
            if (string.IsNullOrEmpty(serialized)) return null;
            byte[] raw = Encoding.UTF8.GetBytes(serialized);
            if (raw.LongLength > MaxTransferBytes)
            {
                KmhLog.Warn($"Transport: '{kind}' is {raw.LongLength} bytes, past the transfer ceiling - not sent.");
                return null;
            }

            int chunk = Math.Min(RawChunkBytes, Math.Max(1024, safeFrameBytes / 2));
            int total = (int)((raw.LongLength + chunk - 1) / chunk);
            if (total <= 0 || total > MaxChunks)
            {
                KmhLog.Warn($"Transport: '{kind}' needs {total} fragments, past the {MaxChunks} limit - not sent.");
                return null;
            }

            string tid = Guid.NewGuid().ToString("N");
            string hash = HashOf(raw);
            var outp = new List<KmhEnvelope>(total);
            for (int i = 0; i < total; i++)
            {
                int off = i * chunk;
                int len = Math.Min(chunk, raw.Length - off);
                outp.Add(new KmhEnvelope(Kind, new
                {
                    tid, kind, i, n = total, len = raw.Length, hash,
                    data = Convert.ToBase64String(raw, off, len),
                }));
            }
            return outp;
        }

        public static string HashOf(byte[] raw)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(raw);
                var sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static readonly Dictionary<string, Transfer> _open = new Dictionary<string, Transfer>(StringComparer.Ordinal);

        private sealed class Transfer
        {
            public string Kind;
            public byte[][] Parts;
            public int Have;
            public int TotalBytes;
            public string Hash;
            public DateTime StartedUtc;
        }

        // Returns the reassembled envelope when this fragment completes a transfer, else null.
        public static KmhEnvelope Accept(KmhEnvelope frag)
        {
            if (frag == null) return null;

            string tid  = frag.GetString("tid", "") ?? "";
            string kind = frag.GetString("kind", "") ?? "";
            int i       = frag.GetInt("i", -1);
            int n       = frag.GetInt("n", -1);
            int len     = frag.GetInt("len", -1);
            string hash = frag.GetString("hash", "") ?? "";
            string data = frag.GetString("data", "") ?? "";

            if (tid.Length == 0 || tid.Length > 64 || kind.Length == 0) return Reject(tid, "bad transfer header");
            if (n <= 0 || n > MaxChunks) return Reject(tid, $"bad chunk count {n}");
            if (i < 0 || i >= n) return Reject(tid, $"chunk index {i} outside 0..{n - 1}");
            if (len <= 0 || len > MaxTransferBytes) return Reject(tid, $"declared size {len} out of range");

            Prune();

            if (!_open.TryGetValue(tid, out Transfer t))
            {
                if (_open.Count >= MaxConcurrentTransfers) return Reject(tid, "too many transfers in flight");
                t = new Transfer { Kind = kind, Parts = new byte[n][], TotalBytes = len, Hash = hash, StartedUtc = DateTime.UtcNow };
                _open[tid] = t;
                KmhLog.Protocol($"{kind}: transfer started ({n} fragment(s), {len} bytes)");
            }
            else if (t.Parts.Length != n || t.TotalBytes != len
                     || !string.Equals(t.Kind, kind, StringComparison.Ordinal)
                     || !string.Equals(t.Hash, hash, StringComparison.Ordinal))
                return Reject(tid, "fragment header changed mid-transfer");

            if (t.Parts[i] != null) return null;   // duplicate: ignore

            byte[] piece;
            try { piece = Convert.FromBase64String(data); }
            catch { return Reject(tid, "undecodable chunk"); }

            long soFar = piece.LongLength;
            foreach (byte[] p in t.Parts) if (p != null) soFar += p.LongLength;
            if (soFar > t.TotalBytes) return Reject(tid, "chunks exceed the declared size");

            t.Parts[i] = piece;
            t.Have++;
            KmhLog.Protocol($"{kind}: fragment {t.Have}/{n}");
            if (t.Have < t.Parts.Length) return null;

            _open.Remove(tid);
            var full = new byte[t.TotalBytes];
            int off = 0;
            foreach (byte[] p in t.Parts)
            {
                if (off + p.Length > full.Length) return Reject(tid, "reassembled size overflow");
                Buffer.BlockCopy(p, 0, full, off, p.Length);
                off += p.Length;
            }
            if (off != full.Length) return Reject(tid, "reassembled size mismatch");
            if (!string.Equals(HashOf(full), t.Hash, StringComparison.Ordinal)) return Reject(tid, "hash mismatch");

            KmhEnvelope env = KmhEnvelope.TryParse(Encoding.UTF8.GetString(full));
            if (env == null) return Reject(tid, "reassembled payload did not parse");

            double ms = (DateTime.UtcNow - t.StartedUtc).TotalMilliseconds;
            KmhLog.Debug($"{kind}: assembled {t.TotalBytes} bytes from {t.Parts.Length} fragment(s) in {ms:0}ms");
            return env;
        }

        // A rejected fragment abandons its whole transfer: a partial snapshot must never be applied.
        private static KmhEnvelope Reject(string tid, string why)
        {
            if (!string.IsNullOrEmpty(tid)) _open.Remove(tid);
            KmhLog.Warn($"Transport: dropped fragment transfer - {why}");
            return null;
        }

        private static void Prune()
        {
            if (_open.Count == 0) return;
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-AssemblyTimeoutSeconds);
            List<string> dead = null;
            foreach (KeyValuePair<string, Transfer> kv in _open)
                if (kv.Value.StartedUtc < cutoff) (dead = dead ?? new List<string>()).Add(kv.Key);
            if (dead == null) return;
            foreach (string k in dead)
            {
                KmhLog.Warn($"Transport: fragment transfer for '{_open[k].Kind}' timed out after {AssemblyTimeoutSeconds}s - discarded.");
                _open.Remove(k);
            }
        }

        // A new server owns its own transfers; anything half-assembled belongs to the connection we just left.
        public static void Clear() => _open.Clear();
    }
}
