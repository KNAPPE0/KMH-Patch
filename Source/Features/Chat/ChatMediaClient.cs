using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Chat
{
    // Bytes arrive over the authenticated KMH connection, so the host owning the picture sees the server and never the player.
    internal static class ChatMediaClient
    {
        private enum State { None, Waiting, Ready, Failed }

        // Free of Unity and of the transport, so the integrity rules can be proved outside a running game.
        internal sealed class Assembly
        {
            public string Mime = "";
            public int    Total, Chunks;
            public string Hash = "";
            public string Error;            // non-null once something is wrong; it never recovers

            private byte[][] _parts;
            private int _have, _received;

            // The meta is a claim, so the client's own bounds decide whether to believe it enough to allocate.
            internal static Assembly Begin(string mime, int total, int chunks, string hash, int maxBytes, out string error)
            {
                error = null;
                if (total <= 0 || total > maxBytes) { error = "the prepared media is too large"; return null; }
                if (chunks <= 0 || chunks > 4096)   { error = "the prepared media is malformed"; return null; }
                if (string.IsNullOrEmpty(hash))     { error = "the prepared media has no integrity hash"; return null; }
                return new Assembly { Mime = mime ?? "", Total = total, Chunks = chunks, Hash = hash, _parts = new byte[chunks][] };
            }

            public bool Complete => Error == null && _have == Chunks;

            // Each rejection means the stream is not what the meta described, and none may be quietly tolerated.
            public bool Add(int i, int n, byte[] part)
            {
                if (Error != null) return false;
                if (part == null)         { Error = "the media stream is corrupt"; return false; }
                if (n != Chunks)          { Error = "the media stream changed shape mid-transfer"; return false; }
                if (i < 0 || i >= Chunks) { Error = "the media stream is out of range"; return false; }
                if (_parts[i] != null)    { Error = "the media stream repeated a chunk"; return false; }
                if (_received + part.Length > Total) { Error = "the media stream is longer than declared"; return false; }

                _parts[i] = part;
                _have++;
                _received += part.Length;
                return true;
            }

            // In index order, not arrival order, so out-of-sequence chunks still produce the bytes the server encoded.
            public byte[] Take()
            {
                if (Error != null) return null;
                if (_have != Chunks) { Error = "the media stream is missing a chunk"; return null; }

                byte[] all = new byte[_received];
                int at = 0;
                for (int c = 0; c < Chunks; c++) { Buffer.BlockCopy(_parts[c], 0, all, at, _parts[c].Length); at += _parts[c].Length; }

                if (all.Length != Total) { Error = "the media is the wrong length"; return null; }
                if (!HashMatches(all, Hash)) { Error = "the media failed its integrity check"; return null; }

                _parts = null;
                return all;
            }
        }

        internal static bool HashMatches(byte[] bytes, string expected)
        {
            if (bytes == null || string.IsNullOrEmpty(expected)) return false;
            try
            {
                using SHA256 sha = SHA256.Create();
                byte[] h = sha.ComputeHash(bytes);
                var sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return string.Equals(sb.ToString(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private sealed class Entry
        {
            public State    State;
            public string   Error = "";
            public byte[]   Bytes;
            public string   Mime = "";
            public Assembly Building;
            public float    StartedAt;
            public float    LastUsed;
        }

        private static readonly Dictionary<string, Entry> _byId = new Dictionary<string, Entry>(StringComparer.Ordinal);

        // Kept so an evicted texture rebuilds without asking again, but a session of gifs at MBs each needs a ceiling.
        private const long MaxHeldBytes = 24L * 1024 * 1024;
        private static long _heldBytes;

        private static void EvictHeldBytes()
        {
            while (_heldBytes > MaxHeldBytes)
            {
                string oldest = null; float best = float.MaxValue;
                foreach (KeyValuePair<string, Entry> kv in _byId)
                    if (kv.Value.State == State.Ready && kv.Value.Bytes != null && kv.Value.LastUsed < best)
                    { best = kv.Value.LastUsed; oldest = kv.Key; }
                if (oldest == null) return;

                // Forgotten outright, not left as a failure: asking the server again is what should happen next.
                Forget(oldest);
            }
        }

        // The server has to fetch and convert before it can answer, so this is longer than a plain image request.
        private const float TimeoutSeconds = 45f;

        public static bool Available { get; private set; }

        // From the hello. An older server never sends it, which reads as "no resolver" and leaves the url path alone.
        public static void SetAvailable(bool on)
        {
            Available = on;
            KmhLog.Debug($"Chat media: server resolver {(on ? "available" : "not available")}");
        }

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ChatMediaMeta,  OnMeta);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ChatMediaChunk, OnChunk);
        }

        public static bool IsLoading(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out Entry e) && e.State == State.Waiting;

        public static string FailureFor(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out Entry e) && e.State == State.Failed ? e.Error : null;

        public static bool TryTake(string id, out byte[] bytes, out string mime)
        {
            bytes = null; mime = "";
            if (string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out Entry e) || e.State != State.Ready) return false;
            e.LastUsed = UnityEngine.Time.realtimeSinceStartup;
            bytes = e.Bytes;
            mime = e.Mime;
            return bytes != null;
        }

        // The player asking again. Forgetting first is what lets Request start over, since it ignores a known id.
        public static void Retry(string id)
        {
            if (string.IsNullOrEmpty(id) || !Available) return;
            Forget(id);
            Request(id);
        }

        private static void Forget(string id)
        {
            if (!_byId.TryGetValue(id, out Entry e)) return;
            if (e.Bytes != null) _heldBytes -= e.Bytes.Length;
            _byId.Remove(id);
        }

        // A click, or the player's standing auto-load answer. Never called on its own.
        public static void Request(string id)
        {
            if (string.IsNullOrEmpty(id) || !Available) return;
            if (_byId.ContainsKey(id)) return;   // asking, held, or already answered - only Retry starts over
            _byId[id] = new Entry { State = State.Waiting, StartedAt = UnityEngine.Time.realtimeSinceStartup };
            KmhDispatcher.Send(KmhProtocol.Kind.ChatMediaRequest, new { id });
        }

        public static void Tick()
        {
            if (_byId.Count == 0) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            List<string> late = null;
            foreach (var kv in _byId)
                if (kv.Value.State == State.Waiting && now - kv.Value.StartedAt > TimeoutSeconds)
                    (late ?? (late = new List<string>())).Add(kv.Key);
            if (late == null) return;
            // Collected first: Fail can insert, and mutating the dictionary mid-enumeration would throw.
            foreach (string id in late) Fail(id, "the server did not finish preparing this in time");
        }

        private static void OnMeta(KmhEnvelope env)
        {
            string id = env?.GetString("id") ?? "";
            if (string.IsNullOrEmpty(id)) return;

            if (!env.GetBool("ok"))
            {
                string reason = env.GetString("reason") ?? "unavailable";
                Fail(id, reason == "expired" ? "no longer available - ask for it again" : reason);
                return;
            }

            Assembly building = Assembly.Begin(env.GetString("mime"), env.GetInt("bytes", 0), env.GetInt("chunks", 0),
                                               env.GetString("hash"), ChatImageCache.MaxBytes, out string error);
            if (building == null) { Fail(id, error); return; }

            if (!_byId.TryGetValue(id, out Entry e)) { e = new Entry(); _byId[id] = e; }
            // A second meta for media already held must release the old bytes, or the held total only ever climbs.
            if (e.Bytes != null) { _heldBytes -= e.Bytes.Length; e.Bytes = null; }
            e.State = State.Waiting;
            e.Mime = building.Mime;
            e.Building = building;
            KmhLog.Debug($"Chat media {id}: {building.Mime}, {building.Total} bytes in {building.Chunks} chunk(s), "
                       + $"{env.GetInt("w", 0)}x{env.GetInt("h", 0)}, {env.GetInt("frames", 0)} frame(s)");
        }

        private static void OnChunk(KmhEnvelope env)
        {
            string id = env?.GetString("id") ?? "";
            if (string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out Entry e) || e.Building == null) return;
            if (e.State != State.Waiting) return;

            byte[] part;
            try { part = Convert.FromBase64String(env.GetString("b64") ?? ""); }
            catch { Fail(id, "the media stream is corrupt"); return; }

            if (!e.Building.Add(env.GetInt("i", -1), env.GetInt("n", -1), part)) { Fail(id, e.Building.Error); return; }
            if (!e.Building.Complete) return;

            byte[] all = e.Building.Take();
            if (all == null) { Fail(id, e.Building.Error); return; }

            e.Bytes = all;
            e.State = State.Ready;
            e.LastUsed = UnityEngine.Time.realtimeSinceStartup;
            _heldBytes += all.Length;
            EvictHeldBytes();
            KmhLog.Debug($"Chat media {id}: assembled and verified, {all.Length} bytes");
        }

        private static void Fail(string id, string why)
        {
            if (!_byId.TryGetValue(id, out Entry e)) { e = new Entry(); _byId[id] = e; }
            e.State = State.Failed;
            e.Error = string.IsNullOrEmpty(why) ? "unavailable" : why;
            e.Building = null;
            if (e.Bytes != null) { _heldBytes -= e.Bytes.Length; e.Bytes = null; }
            KmhLog.Debug($"Chat media {id}: {e.Error}");
        }

        public static void Clear()
        {
            _byId.Clear();
            _heldBytes = 0;
            Available = false;
        }
    }
}
