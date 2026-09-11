using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using UnityEngine;

namespace KMHPatch.Features.Chat
{
    // The KMH server fetches a watch link into one finished mp4 and serves that; this client only asks and plays.
    internal static class ChatYouTube
    {
        private const float ServerWaitSeconds = 900f;

        private sealed class Entry
        {
            public string WatchUrl = "";
            public string Playable = "";
            public string Title = "";
            public string Error;              // non-null once it cannot play; cleared only by a fresh resolve
            public bool Loading, PlayWhenReady;
            public float AskedAt;
            public int   Height;
            public string Queued;             // resolved, waiting for the one download slot
            public ChatVideoServer.Answer FromServer;
            public ChatVideoCache.Download Download;
        }

        private static readonly Dictionary<string, Entry> _byId = new Dictionary<string, Entry>(StringComparer.Ordinal);

        private static bool _enabled;
        private static int  _maxHeight = 1080;
        private static int  _maxSeconds;

        // A server that sends nothing leaves this off, so the browser button is always the fallback.
        public static void Configure(bool enabled, int maxHeight, int maxSeconds)
        {
            _maxHeight  = maxHeight <= 0 ? 1080 : Mathf.Clamp(maxHeight, 144, 1080);
            _maxSeconds = maxSeconds < 0 ? 0 : maxSeconds;
            _enabled    = enabled;
        }

        public static bool Available => _enabled;

        public static int ServerMaxHeight => _maxHeight;

        // The player's choice, never above what the owner allows.
        public static int WantedHeight()
        {
            int wanted = 0;
            try { wanted = KMHPatchMod.Settings?.VideoQuality ?? 0; } catch { }
            return HeightWithin(wanted, _maxHeight);
        }

        // Left alone, whatever the owner allows: the server fetches once and every viewer after that waits for nothing.
        internal static int HeightWithin(int wanted, int allowed)
        {
            if (allowed <= 0) allowed = 1080;
            if (wanted <= 0) return allowed;
            return wanted > allowed ? allowed : wanted;
        }

        public static void RequestPlay(string watchUrl)
        {
            string id = VideoIdOf(watchUrl);
            if (!_enabled || id.Length == 0)
            {
                KmhLog.Warn($"YouTube: not playable here - {(id.Length == 0 ? "no video id in the link" : "the server has watch-page playback off")}");
                return;
            }
            // A second click while one is already in flight would start a second download onto the same part-file.
            if (_byId.TryGetValue(id, out Entry busy)
                && (busy.Loading || busy.Download != null || busy.Queued != null)) return;

            KmhLog.Info($"YouTube: recognized {id}");

            // Already pulled down once: nothing is asked of the server at all.
            string have = ChatVideoCache.PathFor(id, WantedHeight());
            if (ChatVideoCache.Ready(have))
            {
                ChatVideoCache.Touch(have);
                var cached = new Entry { WatchUrl = watchUrl, Playable = ChatVideoCache.PlayableUrl(have) };
                _byId[id] = cached;
                ChatVideoPlayer.Play(watchUrl, cached.Playable, _maxSeconds);
                return;
            }

            if (!ChatVideoServer.Available)
            {
                _byId[id] = new Entry { WatchUrl = watchUrl, Error = "Can't play here: this server does not serve video" };
                return;
            }

            // Pinned now: changing quality mid-fetch would file the video that arrives under the new height's name.
            var entry = new Entry
            {
                WatchUrl = watchUrl, Loading = true, PlayWhenReady = true,
                AskedAt = Time.realtimeSinceStartup, Height = WantedHeight(),
            };
            _byId[id] = entry;
            entry.FromServer = ChatVideoServer.Resolve(watchUrl, entry.Height);
        }

        public static string StatusFor(string watchUrl)
        {
            string id = VideoIdOf(watchUrl);
            if (id.Length == 0 || !_byId.TryGetValue(id, out Entry e)) return null;
            if (e.Download != null)
                return $"Downloading… {Mathf.RoundToInt(e.Download.Progress * 100f)}% ({e.Download.Megabytes} MB) - click to stop";
            if (e.Queued != null) return "Waiting for another download to finish… - click to cancel";
            if (e.Loading) return "The server is fetching it…";
            return e.Error;
        }

        // A player who clicked play on a two-gigabyte video needs a way out that is not quitting the game.
        public static bool CanStop(string watchUrl)
        {
            string id = VideoIdOf(watchUrl);
            return id.Length > 0 && _byId.TryGetValue(id, out Entry e) && (e.Download != null || e.Queued != null);
        }

        public static void Stop(string watchUrl)
        {
            string id = VideoIdOf(watchUrl);
            if (id.Length == 0 || !_byId.TryGetValue(id, out Entry e)) return;
            if (e.Download == null && e.Queued == null) return;

            if (e.Download != null)
            {
                ChatVideoCache.Cancel(e.Download);   // the download's own thread removes the part once it lets go
            }
            e.Download = null;
            e.Queued = null;
            e.Loading = false;
            e.PlayWhenReady = false;
            e.Error = "Stopped.";
            KmhLog.Info($"YouTube: {id} - stopped by the player");
        }

        public static string TitleFor(string watchUrl)
        {
            string id = VideoIdOf(watchUrl);
            return id.Length > 0 && _byId.TryGetValue(id, out Entry e) ? e.Title ?? "" : "";
        }

        public static void Clear()
        {
            foreach (KeyValuePair<string, Entry> kv in _byId) ChatVideoCache.Cancel(kv.Value.Download);
            _byId.Clear();
        }

        internal static bool Downloading()
        {
            foreach (KeyValuePair<string, Entry> kv in _byId) if (kv.Value.Download != null) return true;
            return false;
        }

        // A resolve that finished while another download held the slot; started as soon as that one is out of the way.
        private static void StartQueued()
        {
            if (Downloading()) return;
            foreach (KeyValuePair<string, Entry> kv in _byId)
            {
                Entry e = kv.Value;
                if (e.Queued == null) continue;
                string url = e.Queued;
                e.Queued = null;
                KmhLog.Info($"YouTube: {kv.Key} is ready on the KMH server, pulling it down");
                e.Download = ChatVideoCache.Begin(url, kv.Key, e.Height);
                return;
            }
        }

        // Polled from the same pump as the image cache. No coroutine: RimWorld gives a mod no MonoBehaviour to run one on.
        public static void Tick()
        {
            CollectDownloads();
            StartQueued();

            foreach (KeyValuePair<string, Entry> kv in _byId)
            {
                Entry e = kv.Value;
                if (e.FromServer != null && !e.FromServer.Done
                    && Time.realtimeSinceStartup - e.AskedAt > ServerWaitSeconds)
                {
                    ChatVideoServer.Forget(e.WatchUrl);
                    e.FromServer.Error = "the server did not answer";
                    e.FromServer.Done  = true;
                }
                if (e.FromServer == null || !e.FromServer.Done) continue;

                ChatVideoServer.Answer got = e.FromServer;
                e.FromServer = null;
                if (got.Title.Length > 0) e.Title = got.Title;

                if (got.Error.Length > 0 || got.Video.Length == 0)
                {
                    e.Loading = false;
                    KmhLog.Warn($"YouTube: the server could not serve {kv.Key} - {got.Error}");
                    e.Error = "Can't play here: " + got.Error;
                    return;
                }

                // One download at a time, like the image cache: two of these at once is a gigabyte of contention.
                if (Downloading()) { e.Queued = got.Video; return; }

                KmhLog.Info($"YouTube: {kv.Key} is ready on the KMH server, pulling it down");
                e.Download = ChatVideoCache.Begin(got.Video, kv.Key, e.Height);
                return;
            }
        }

        private static void CollectDownloads()
        {
            foreach (KeyValuePair<string, Entry> kv in _byId)
            {
                Entry e = kv.Value;
                if (e.Download == null || !ChatVideoCache.Poll(e.Download)) continue;

                ChatVideoCache.Download got = e.Download;
                e.Download = null;
                e.Loading = false;

                if (got.Error.Length > 0)
                {
                    KmhLog.Warn($"YouTube: could not pull down {kv.Key} - {got.Error}");
                    e.Error = "Can't play here: " + got.Error;
                    return;
                }

                e.Playable = ChatVideoCache.PlayableUrl(got.File);
                e.Error = null;
                KmhLog.Info($"YouTube: {kv.Key} is on disk, playing from there");
                if (e.PlayWhenReady)
                {
                    e.PlayWhenReady = false;
                    ChatVideoPlayer.Play(e.WatchUrl, e.Playable, _maxSeconds);
                }
                return;
            }
        }

        // Strict about the id's shape because it keys a fetch and rides a request.
        internal static string VideoIdOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            Uri u;
            try { if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out u) || u == null) return ""; }
            catch { return ""; }
            if (u.Scheme != "http" && u.Scheme != "https") return "";

            string host = (u.Host ?? "").ToLowerInvariant();
            string path = u.AbsolutePath ?? "";

            if (host == "youtu.be") return Clean(path.TrimStart('/'));
            if (host != "youtube.com" && !host.EndsWith(".youtube.com", StringComparison.Ordinal)) return "";

            foreach (string prefix in new[] { "/shorts/", "/embed/", "/live/", "/v/" })
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return Clean(path.Substring(prefix.Length));

            if (!path.Equals("/watch", StringComparison.OrdinalIgnoreCase)) return "";
            return Clean(QueryValue(u.Query, "v"));
        }

        // Where a link asks playback to begin, in seconds - "?t=90", "?t=1m30s", "&start=90".
        internal static int StartSecondsOf(string url)
        {
            Uri u;
            try { if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u) || u == null) return 0; }
            catch { return 0; }

            string t = QueryValue(u.Query, "t");
            if (t.Length == 0) t = QueryValue(u.Query, "start");
            if (t.Length == 0) return 0;

            int total = 0, digits = 0;
            foreach (char c in t)
            {
                if (c >= '0' && c <= '9') { digits = digits * 10 + (c - '0'); continue; }
                if (c == 'h') { total += digits * 3600; digits = 0; continue; }
                if (c == 'm') { total += digits * 60; digits = 0; continue; }
                if (c == 's') { total += digits; digits = 0; continue; }
                return 0;   // not a form this understands, and a wrong offset is worse than none
            }
            return total + digits;
        }

        private static string QueryValue(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return "";
            foreach (string pair in query.TrimStart('?').Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(pair.Substring(0, eq), key, StringComparison.Ordinal)) continue;
                return Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
            return "";
        }

        private static string Clean(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int cut = id.IndexOfAny(new[] { '/', '?', '&', '#' });
            if (cut >= 0) id = id.Substring(0, cut);
            if (id.Length != 11) return "";
            foreach (char c in id)
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return "";
            return id;
        }
    }
}
