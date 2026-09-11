using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;

namespace KMHPatch.Features.Chat
{
    // Fetched only on a click, because fetching tells that host the player's IP: one at a time, capped, timed out, LRU.
    internal static class ChatImageCache
    {
        // A chat log scrolls past hundreds of images; holding them all is how a mod gets blamed for a memory leak.
        private const int   MaxCached      = 6;
        private const float TimeoutSeconds = 15f;

        // Six ENTRIES bounds nothing once they animate: six long gifs would be most of a gigabyte. 48M px ~ 190MB.
        private const long MaxCachedPixels = 48L * 1024 * 1024;

        // The cap is the SERVER's number from the hello, clamped on arrival because it arrives over the wire.
        private const int DefaultMaxBytes =  4 * 1024 * 1024;
        private const int FloorMaxBytes   =       256 * 1024;
        private const int CeilingMaxBytes = 32 * 1024 * 1024;

        private static int _maxBytes = DefaultMaxBytes;

        public static int MaxBytes => _maxBytes;

        // From the hello. 0/absent means the server never said, so KMH keeps its own conservative default.
        public static void SetServerMaxBytes(int bytes)
        {
            _maxBytes = ClampMaxBytes(bytes);
            KmhLog.Debug($"Chat image: size cap set to {_maxBytes / 1024}KB (server said {bytes})");
        }

        // Held between a floor and a ceiling: a client must not be talked into holding an arbitrary amount of data.
        internal static int ClampMaxBytes(int bytes)
        {
            if (bytes <= 0) return DefaultMaxBytes;
            if (bytes < FloorMaxBytes) return FloorMaxBytes;
            if (bytes > CeilingMaxBytes) return CeilingMaxBytes;
            return bytes;
        }

        private enum State { None, Loading, Ready, Failed }

        private sealed class Entry
        {
            public State     State;
            public Texture2D Texture;        // still images
            public KmhGif.Animation Gif;     // animated gifs - several textures and their frame timings
            public string    Error = "";
            public bool      Permanent;      // the host's answer will not change, so not even a click should ask again
            public float     LastUsed;
            public float     StartedAt;      // when this animation began, so every viewer sees the same frame

            public Texture2D Current
                => Gif != null ? Gif.FrameAt(Time.realtimeSinceStartup - StartedAt) : Texture;

            public bool HasImage => Gif != null ? Gif.Frames.Count > 0 : Texture != null;

            // An animation holds a texture PER FRAME, so six of them is not six pictures' worth of memory.
            public long Pixels
                => Gif != null ? (long)Gif.Width * Gif.Height * Gif.Frames.Count
                 : Texture != null ? (long)Texture.width * Texture.height : 0L;
        }

        private static readonly Dictionary<string, Entry> _byUrl = new Dictionary<string, Entry>(StringComparer.Ordinal);

        // One at a time: parallel downloads on a game thread are how a chat window turns into a stall.
        private static UnityWebRequest _active;
        private static string          _activeUrl;
        private static float           _startedAt;

        // A key is either a URL this client fetches or a RESOLVER ID the server converted - the privacy distinction.
        internal static bool IsResolvedId(string key)
            => !string.IsNullOrEmpty(key) && key.IndexOf("://", StringComparison.Ordinal) < 0;

        // For an animation this hands back a DIFFERENT texture as time passes, so callers never have to know.
        public static bool IsReady(string url, out Texture2D tex)
        {
            tex = null;
            if (string.IsNullOrEmpty(url)) return false;
            if (IsResolvedId(url)) AdoptResolved(url);
            if (!_byUrl.TryGetValue(url, out Entry e) || e.State != State.Ready || !e.HasImage) return false;
            e.LastUsed = Time.realtimeSinceStartup;
            tex = e.Current;
            return tex != null;
        }

        // The still size of an image, so layout can reserve a constant height instead of resizing every frame.
        public static bool TrySize(string url, out int w, out int h)
        {
            w = h = 0;
            if (string.IsNullOrEmpty(url) || !_byUrl.TryGetValue(url, out Entry e) || e.State != State.Ready) return false;
            if (e.Gif != null) { w = e.Gif.Width; h = e.Gif.Height; }
            else if (e.Texture != null) { w = e.Texture.width; h = e.Texture.height; }
            return w > 0 && h > 0;
        }

        public static bool IsAnimated(string url)
            => !string.IsNullOrEmpty(url) && _byUrl.TryGetValue(url, out Entry e) && e.Gif != null && e.Gif.IsAnimated;

        public static bool IsLoading(string url)
            => !string.IsNullOrEmpty(url)
               && (IsResolvedId(url)
                   ? ChatMediaClient.IsLoading(url)
                   : _byUrl.TryGetValue(url, out Entry e) && e.State == State.Loading);

        public static string FailureFor(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (_byUrl.TryGetValue(url, out Entry e) && e.State == State.Failed) return e.Error;
            return IsResolvedId(url) ? ChatMediaClient.FailureFor(url) : null;
        }

        // Server-converted bytes land in the same cache, so nothing that draws knows which path they came from.
        private static void AdoptResolved(string id)
        {
            if (_byUrl.TryGetValue(id, out Entry have) && have.State == State.Ready) return;
            if (!ChatMediaClient.TryTake(id, out byte[] bytes, out string mime) || bytes == null) return;

            if (!_byUrl.ContainsKey(id)) _byUrl[id] = new Entry { State = State.None, LastUsed = Time.realtimeSinceStartup };
            KmhLog.Debug($"Chat media {id}: adopting {bytes.Length} bytes ({mime})");
            if (KmhGif.LooksLikeGif(bytes)) StoreGif(id, bytes);
            else if (KmhApng.LooksLikeApng(bytes)) StoreApng(id, bytes);
            else StoreStill(id, bytes);
        }

        // A host that answered 404 answers 404 again: auto-load asked once per frame and hammered one dead link for a minute.
        public static bool CanRetry(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            // Resolved media is asked for over KMH's own connection, where a retry costs no third party anything.
            if (IsResolvedId(url)) return ChatMediaClient.FailureFor(url) != null;
            return _byUrl.TryGetValue(url, out Entry e) && e.State == State.Failed && !e.Permanent;
        }

        // The player asking again, which is the only thing that clears a failure. A permanent one stays failed.
        public static void Retry(string url)
        {
            if (!CanRetry(url)) return;
            // Left in place: the adopt path overwrites this entry when the bytes land, so no texture is orphaned.
            if (IsResolvedId(url)) { ChatMediaClient.Retry(url); return; }
            _byUrl[url].State = State.None;
            _byUrl[url].Error = "";
            if (_active == null) Begin(url);
        }

        // Idempotent, and a FAILED entry stays failed: only Retry clears one, or a drawn row becomes a request loop.
        public static void Request(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            // A resolver id is asked for over KMH's own connection; nothing here contacts the media's host.
            if (IsResolvedId(url)) { ChatMediaClient.Request(url); return; }
            if (_byUrl.TryGetValue(url, out Entry existing))
            {
                if (existing.State != State.None) return;
            }
            else _byUrl[url] = new Entry { State = State.None, LastUsed = Time.realtimeSinceStartup };

            if (_active != null) return;   // queued behind the one in flight; the next Tick picks it up
            Begin(url);
        }

        private static void Begin(string url)
        {
            try
            {
                // Raw bytes, not UnityWebRequestTexture: that decodes PNG/JPG only and hands back nothing for a gif.
                _active = UnityWebRequest.Get(url);
                _active.timeout = (int)TimeoutSeconds;
                _activeUrl = url;
                _startedAt = Time.realtimeSinceStartup;
                _byUrl[url].State = State.Loading;
                _active.SendWebRequest();
            }
            catch (Exception ex)
            {
                Fail(url, ex.Message);
            }
        }

        // Polled once per frame from the KMH pump. No coroutine: RimWorld gives a mod no MonoBehaviour for one.
        public static void Tick()
        {
            ChatMediaClient.Tick();

            if (_active == null)
            {
                StartNextPending();
                return;
            }

            if (!_active.isDone)
            {
                // Cut off MID-FLIGHT: checking after isDone held the whole file in memory before rejecting it.
                if (_active.downloadedBytes > (ulong)_maxBytes)
                {
                    Fail(_activeUrl, $"too large - {_active.downloadedBytes / (1024 * 1024)}MB, over the server's {_maxBytes / (1024 * 1024)}MB limit", true);
                    Cleanup();
                    return;
                }
                // The request's own timeout is the primary guard; this catches the case where it never fires.
                if (Time.realtimeSinceStartup - _startedAt > TimeoutSeconds * 2f)
                {
                    Fail(_activeUrl, "timed out");
                    Cleanup();
                }
                return;
            }

            try
            {
                long   status      = _active.responseCode;
                string contentType  = ContentTypeOf(_active);
                byte[] bytes        = _active.downloadHandler?.data;
                int    size         = bytes?.Length ?? 0;

                // Guarded, not just gated inside Debug: naming the format sniffs the bytes on every picture.
                if (KmhLog.DebugEnabled)
                    KmhLog.Debug($"Chat image: {HostOf(_activeUrl)} -> HTTP {status}, type={contentType}, {size} bytes, "
                               + $"format={DescribeFormat(bytes)}");

                if (_active.result != UnityWebRequest.Result.Success)
                {
                    // 415 in particular is Discord's proxy refusing a conversion - a server-side answer, not a network one.
                    Fail(_activeUrl, HttpFailure(status, _active.error), IsPermanentStatus(status));
                }
                else if (size > _maxBytes)
                {
                    Fail(_activeUrl, $"too large - {size / (1024 * 1024)}MB, over the server's {_maxBytes / (1024 * 1024)}MB limit", true);
                }
                else if (bytes == null || size == 0)
                {
                    Fail(_activeUrl, "empty response");
                }
                else if (LooksLikeHtml(bytes, contentType))
                {
                    // The server should send no share page, but one reaching an image decoder must not read as a corrupt picture.
                    Fail(_activeUrl, "that link is a web page, not an image file", true);
                }
                else if (KmhGif.LooksLikeGif(bytes))
                {
                    StoreGif(_activeUrl, bytes);
                }
                else if (KmhApng.LooksLikeApng(bytes))
                {
                    StoreApng(_activeUrl, bytes);
                }
                else
                {
                    // Naming the format matters: 'not a readable image' told a player nothing and looked like a KMH bug.
                    string unsupported = UnsupportedFormat(bytes);
                    if (unsupported != null) Fail(_activeUrl, unsupported, true);
                    else StoreStill(_activeUrl, bytes);
                }
            }
            catch (Exception ex) { Fail(_activeUrl, ex.Message); }
            finally { Cleanup(); }
        }

        // Nothing here decodes webp, and a video belongs to the player. By magic number - Discord serves webp as .png.
        private static string UnsupportedFormat(byte[] b)
        {
            if (b == null || b.Length < 12) return null;

            if (IsWebp(b))
            {
                // Animated and static webp are different problems: Discord's proxy answers 415 for a gif of an animated one.
                return IsAnimatedWebp(b)
                    ? "animated WebP - RimWorld has no decoder for it, and Discord's proxy cannot convert one to GIF"
                    : "WebP - RimWorld has no decoder for it; the server can ask Discord's proxy for a PNG instead";
            }

            // ....ftyp - an ISO media container (mp4/mov)
            if (b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70)
                return "this is a video, not an image";

            // 1A 45 DF A3 - Matroska / WebM
            if (b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3)
                return "this is a video, not an image";

            return null;   // let Unity try - it may well be a png or jpg
        }

        // RIFF....WEBP
        private static bool IsWebp(byte[] b)
            => b != null && b.Length >= 12
               && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
               && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50;

        // An animated webp carries per-frame ANMF chunks; a static one is a server-side choice that can be fixed.
        internal static bool IsAnimatedWebp(byte[] b)
        {
            if (!IsWebp(b)) return false;
            int limit = Math.Min(b.Length - 4, 512 * 1024);   // the first ANMF is near the front; do not scan a whole file
            for (int i = 12; i < limit; i++)
                if (b[i] == 0x41 && b[i + 1] == 0x4E && b[i + 2] == 0x4D && b[i + 3] == 0x46) return true;   // "ANMF"
            return false;
        }

        // A share link answers with a page, and feeding that to a texture loader read as 'not a readable image'.
        internal static bool LooksLikeHtml(byte[] b, string contentType)
        {
            if (!string.IsNullOrEmpty(contentType)
                && contentType.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (b == null || b.Length < 5) return false;

            int i = 0;
            while (i < b.Length && i < 16 && (b[i] == 0x20 || b[i] == 0x09 || b[i] == 0x0A || b[i] == 0x0D || b[i] == 0xEF || b[i] == 0xBB || b[i] == 0xBF)) i++;
            if (i + 5 > b.Length || b[i] != (byte)'<') return false;

            string head = System.Text.Encoding.ASCII.GetString(b, i, Math.Min(64, b.Length - i)).ToLowerInvariant();
            return head.StartsWith("<!doctype") || head.StartsWith("<html") || head.StartsWith("<head") || head.StartsWith("<?xml");
        }

        // Status codes worth naming. Anything else keeps Unity's own message.
        internal static string HttpFailure(long status, string unityError)
        {
            switch (status)
            {
                case 415: return "the host refused that format (HTTP 415) - the server asked it for a conversion it cannot do";
                case 403: return "the host refused the request (HTTP 403) - it blocks direct fetches";
                case 404: return "the host no longer has that image (HTTP 404)";
                case 429: return "the host is rate-limiting this (HTTP 429) - try again shortly";
            }
            if (status >= 500) return $"the host had an error (HTTP {status})";
            if (status >= 400) return $"the host refused the request (HTTP {status})";
            return string.IsNullOrEmpty(unityError) ? "request failed" : unityError;
        }

        // For the log only.
        internal static string DescribeFormat(byte[] b)
        {
            if (b == null || b.Length < 12) return "too short";
            if (KmhGif.LooksLikeGif(b)) return "gif";
            if (IsWebp(b)) return IsAnimatedWebp(b) ? "webp/animated" : "webp/static";
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return KmhApng.LooksLikeApng(b) ? "png/animated" : "png";
            if (b[0] == 0xFF && b[1] == 0xD8) return "jpeg";
            if (b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70) return "mp4/mov";
            if (b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return "webm";
            if (LooksLikeHtml(b, null)) return "html";
            return "unrecognised";
        }

        private static string ContentTypeOf(UnityWebRequest req)
        {
            try { return req?.GetResponseHeader("Content-Type") ?? ""; } catch { return ""; }
        }
        private static void StoreGif(string url, byte[] bytes)
        {
            KmhGif.Animation gif = KmhGif.Decode(bytes);
            if (gif == null || gif.Frames.Count == 0) { Fail(url, "could not decode this gif", true); return; }
            KmhLog.Debug($"Chat image: decoded gif {gif.Width}x{gif.Height}, {gif.Frames.Count} frame(s), "
                       + $"{(gif.IsAnimated ? "animated" : "single frame")}");

            Entry e = _byUrl[url];
            e.State = State.Ready; e.Gif = gif;
            e.StartedAt = Time.realtimeSinceStartup;
            e.LastUsed  = e.StartedAt;
            Evict();
        }

        // Falls back to the still rather than failing: one frame of a sticker is worth more than an error.
        private static void StoreApng(string url, byte[] bytes)
        {
            KmhGif.Animation anim = KmhApng.Decode(bytes);
            if (anim == null || anim.Frames.Count == 0) { StoreStill(url, bytes); return; }
            KmhLog.Debug($"Chat image: decoded animated png {anim.Width}x{anim.Height}, {anim.Frames.Count} frame(s)");

            Entry e = _byUrl[url];
            e.State = State.Ready; e.Gif = anim;
            e.StartedAt = Time.realtimeSinceStartup;
            e.LastUsed  = e.StartedAt;
            Evict();
        }

        private static void StoreStill(string url, byte[] bytes)
        {
            // mipChain off: drawn at roughly its own size, so mips cost memory for a sharpness nobody sees.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                try { UnityEngine.Object.Destroy(tex); } catch { }
                Fail(url, "not a readable image", true);
                return;
            }
            Entry e = _byUrl[url];
            e.State = State.Ready; e.Texture = tex; e.LastUsed = Time.realtimeSinceStartup;
            Evict();
        }

        private static void StartNextPending()
        {
            foreach (var kv in _byUrl)
                if (kv.Value.State == State.None) { Begin(kv.Key); return; }
        }

        private static void Fail(string url, string why, bool permanent = false)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!_byUrl.TryGetValue(url, out Entry e)) { e = new Entry(); _byUrl[url] = e; }
            e.State = State.Failed;
            e.Permanent = permanent;
            e.Error = string.IsNullOrEmpty(why) ? "could not load" : why;
            KmhLog.Debug($"Chat image: {url} - {e.Error}");
        }

        // A timeout, a 429 or a 5xx can come good; the rest are the host's settled answer about this url.
        internal static bool IsPermanentStatus(long status)
        {
            if (status == 429 || status >= 500) return false;
            return status == 400 || status == 401 || status == 403 || status == 404
                || status == 410 || status == 415 || status == 451;
        }

        private static void Cleanup()
        {
            try { _active?.Dispose(); } catch { }
            _active = null; _activeUrl = null;
        }

        // Least-recently-drawn wins, and the texture is DESTROYED - Unity textures are not garbage collected.
        private static void Evict()
        {
            while (CountReady() > MaxCached || ReadyPixels() > MaxCachedPixels)
            {
                string oldest = null; float best = float.MaxValue;
                foreach (var kv in _byUrl)
                    if (kv.Value.State == State.Ready && kv.Value.LastUsed < best) { best = kv.Value.LastUsed; oldest = kv.Key; }
                if (oldest == null) return;
                Destroy(_byUrl[oldest]);
                _byUrl.Remove(oldest);
            }
        }

        private static int CountReady()
        {
            int n = 0;
            foreach (var kv in _byUrl) if (kv.Value.State == State.Ready) n++;
            return n;
        }

        private static long ReadyPixels()
        {
            long n = 0;
            foreach (var kv in _byUrl) if (kv.Value.State == State.Ready) n += kv.Value.Pixels;
            return n;
        }

        // A gif holds one texture PER FRAME: dropping the reference alone would leak every one of them.
        private static void Destroy(Entry e)
        {
            if (e == null) return;
            if (e.Texture != null)
            {
                try { UnityEngine.Object.Destroy(e.Texture); } catch { }
                e.Texture = null;
            }
            if (e.Gif != null) { e.Gif.Dispose(); e.Gif = null; }
        }

        // Server switch, or the player turning previews off. Everything goes, textures included.
        public static void Clear()
        {
            foreach (var kv in _byUrl) Destroy(kv.Value);
            _byUrl.Clear();
            Cleanup();
        }

        // A player deciding whether to contact a host should be told which one; the path is noise.
        public static string HostOf(string url)
        {
            // Resolved media never leaves the KMH connection, so naming a third-party host would be a lie.
            if (IsResolvedId(url)) return "this server";
            try { return new Uri(url).Host; } catch { return "an external host"; }
        }
    }
}
