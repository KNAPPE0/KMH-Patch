using System;
using System.Collections.Generic;
using System.IO;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Chat
{
    internal static class ChatVideoCache
    {
        private const int DefaultCacheGigabytes = 2;
        private const int MaxCacheDays = 7;

        // A download that stops moving is dead, not slow: without this the row sits on a percentage for ever.
        private const float StallSeconds = 90f;

        internal sealed class Download
        {
            public string File = "", Error = "";
            public bool Done;
            public float Progress;
            public float MovedAt;
            public long  Megabytes;

            // Written by the worker thread and read by Poll on the main one; the counters go through Interlocked.
            internal volatile bool   Cancelled;
            internal volatile bool   WorkerDone;
            internal volatile string WorkerError;
            internal volatile bool   TooLarge;
            internal long Read, Total;
            // Bytes, not percentage, count as movement: a size-less or huge stream barely moves a percentage.
            internal long SeenRead = -1;
            // Fixed when the download starts, so a settings change mid-download cannot lift it.
            internal long MaxBytes;
        }

        // Only a test sets this: a sweep run against the real folder would evict what a player is actually keeping.
        internal static string FolderOverride;

        internal static string Folder
        {
            get
            {
                if (!string.IsNullOrEmpty(FolderOverride)) return FolderOverride;
                try { return Path.Combine(Path.GetTempPath(), "KMH-Video"); }
                catch { return "KMH-Video"; }
            }
        }

        internal static string PathFor(string videoId, int height)
            => Path.Combine(Folder, Safe(videoId) + "_" + Math.Max(0, height) + ".mp4");

        internal static string Safe(string videoId)
        {
            if (string.IsNullOrEmpty(videoId)) return "unknown";
            var sb = new System.Text.StringBuilder(videoId.Length);
            foreach (char c in videoId)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        // A part-file beside it means the last attempt died: that copy plays as a truncated video.
        internal static bool Ready(string file)
            => !string.IsNullOrEmpty(file) && File.Exists(file) && !File.Exists(file + ".part");

        internal static string PlayableUrl(string file) => "file://" + (file ?? "").Replace('\\', '/');

        // The only file: url the player may open is one of this cache's own downloads.
        internal static bool IsCachedPlayable(string url)
        {
            const string scheme = "file://";
            if (string.IsNullOrEmpty(url) || !url.StartsWith(scheme, StringComparison.Ordinal)) return false;
            try
            {
                string full = Path.GetFullPath(url.Substring(scheme.Length));
                string root = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // Eviction is by last USE, not by when it arrived, so what is being watched is the last thing thrown away.
        internal static void Touch(string file)
        {
            try { if (File.Exists(file)) File.SetLastWriteTimeUtc(file, DateTime.UtcNow); } catch { }
        }

        private static long _bytes = -1;

        // Remembered: this is read by a settings row, which redraws many times a second, and it walks a directory.
        internal static long Bytes()
        {
            if (_bytes >= 0) return _bytes;
            try
            {
                _bytes = 0;
                if (!Directory.Exists(Folder)) return 0;
                foreach (FileInfo f in new DirectoryInfo(Folder).GetFiles()) _bytes += f.Length;
            }
            catch { _bytes = 0; }
            return _bytes;
        }

        private static void Forget() => _bytes = -1;

        public static void Clear()
        {
            Forget();
            try { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); }
            catch (Exception ex) { try { KmhLog.Warn($"Video cache: could not clear - {ex.Message}"); } catch { } }
        }

        // Pushed in, not read from settings here: reaching into the loader would drag Verse into an offline sweep.
        internal static int CacheGigabytes = DefaultCacheGigabytes;

        private static long CacheLimit => (long)Math.Max(1, CacheGigabytes) * 1024 * 1024 * 1024;

        // A generous ~10 Mbit/s at 1080p scaled by height, so only a relay sending far too much is stopped.
        internal static long CeilingFor(int height, int maxSeconds)
        {
            long limit = CacheLimit;
            if (maxSeconds <= 0 || height <= 0) return limit;
            long perSecond = Math.Max(360, height) * 1200L;
            long ceiling = Math.Max(64L * 1024 * 1024, perSecond * maxSeconds);
            return Math.Min(limit, ceiling);
        }

        // Not UnityWebRequest: this Unity build refuses a plain-http url outright, and the relay speaks http.
        public static Download Begin(string streamUrl, string videoId, int height, int maxSeconds = 0)
        {
            var d = new Download { File = PathFor(videoId, height), MaxBytes = CeilingFor(height, maxSeconds) };
            if (Ready(d.File)) { d.Done = true; d.Progress = 1f; return d; }

            string part = d.File + ".part";
            try
            {
                Directory.CreateDirectory(Folder);
                try { if (File.Exists(part)) File.Delete(part); } catch { }
            }
            catch (Exception ex) { d.Error = ex.Message; d.Done = true; return d; }

            new System.Threading.Thread(() => Fetch(d, streamUrl, part))
            { IsBackground = true, Name = "KMH video download" }.Start();
            return d;
        }

        // Off the main thread, and touching nothing Unity owns: only the fields above, and only through Interlocked.
        private static void Fetch(Download d, string streamUrl, string part)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(streamUrl);
                // The relay answers 200, 206 or an error and never redirects; a redirect would aim this download anywhere.
                req.AllowAutoRedirect = false;
                req.Timeout = 30000;
                req.ReadWriteTimeout = 30000;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    if ((int)resp.StatusCode >= 300) { d.WorkerError = $"the video relay answered {(int)resp.StatusCode}"; return; }
                    Receive(d, resp.ContentLength, resp.GetResponseStream, part);
                }
            }
            catch (Exception ex) { d.WorkerError = ex.Message; }
            finally
            {
                // The stream is closed by here, so this is the one place a cancelled part-file can actually be removed.
                if (d.Cancelled || d.TooLarge) { try { File.Delete(part); } catch { } }
                d.WorkerDone = true;
            }
        }

        internal static void Receive(Download d, long declared, Func<Stream> open, string part)
        {
            if (declared > d.MaxBytes) { d.TooLarge = true; return; }
            System.Threading.Interlocked.Exchange(ref d.Total, declared);
            using (Stream net = open()) Copy(d, net, part);
        }

        // Stops at the ceiling whatever the headers said: the cache trim only runs once a download has finished.
        internal static void Copy(Download d, Stream net, string part)
        {
            using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buf = new byte[64 * 1024];
                int n;
                long written = 0;
                while (net != null && (n = net.Read(buf, 0, buf.Length)) > 0)
                {
                    if (d.Cancelled) break;
                    if (written + n > d.MaxBytes) { d.TooLarge = true; break; }
                    file.Write(buf, 0, n);
                    written += n;
                    System.Threading.Interlocked.Add(ref d.Read, n);
                }
            }
        }

        public static bool Poll(Download d)
            => d == null || d.Done || Poll(d, UnityEngine.Time.realtimeSinceStartup);

        internal static bool Poll(Download d, float now)
        {
            if (d == null || d.Done) return true;

            long  read  = System.Threading.Interlocked.Read(ref d.Read);
            long  total = System.Threading.Interlocked.Read(ref d.Total);

            float progress = total > 0 ? Math.Min(1f, (float)((double)read / total)) : 0f;
            if (read != d.SeenRead || d.MovedAt <= 0f) { d.MovedAt = now; d.SeenRead = read; }
            d.Progress  = progress;
            d.Megabytes = read / (1024L * 1024L);

            // The worker still owns the file until it says otherwise, so a stall asks it to stop rather than deleting.
            if (!d.WorkerDone)
            {
                if (!d.Cancelled && now - d.MovedAt >= StallSeconds) d.Cancelled = true;
                return false;
            }

            string part = d.File + ".part";
            try
            {
                string err = d.TooLarge
                    ? UI.KmhText.Get("KMH_Video_TooLarge", "the download grew past the size limit for this video and was stopped")
                    : d.Cancelled ? "the download stopped part way" : d.WorkerError;
                if (!string.IsNullOrEmpty(err))
                {
                    d.Error = err;
                    try { if (File.Exists(part)) File.Delete(part); } catch { }
                }
                else
                {
                    Persistence.KmhAtomicFile.Promote(part, d.File);
                    d.Progress = 1f;
                    Trim();
                }
            }
            catch (Exception ex) { d.Error = ex.Message; }
            finally { d.Done = true; }
            return true;
        }

        // Only ever a request: the worker closes the file and removes the part itself, so nothing here can race it.
        public static void Cancel(Download d)
        {
            if (d == null || d.Done) return;
            d.Cancelled = true;
        }

        // Also run at startup, or a cache left by an old session sits there for weeks.
        internal static void Trim()
        {
            Forget();
            try
            {
                if (!Directory.Exists(Folder)) return;
                var files = new List<FileInfo>(new DirectoryInfo(Folder).GetFiles("*.mp4"));
                files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));

                long limit = CacheLimit;
                long total = 0;
                foreach (FileInfo f in files) total += f.Length;

                DateTime cutoff = DateTime.UtcNow.AddDays(-MaxCacheDays);
                foreach (FileInfo f in files)
                {
                    if (f.LastWriteTimeUtc >= cutoff && total <= limit) continue;
                    long size = f.Length;
                    try { f.Delete(); total -= size; } catch { }
                }

                // A part-file with no download behind it is one that died with the last session.
                foreach (FileInfo f in new DirectoryInfo(Folder).GetFiles("*.part"))
                {
                    if (f.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-2)) continue;
                    try { f.Delete(); } catch { }
                }
            }
            catch (Exception ex) { try { KmhLog.Warn($"Video cache trim: {ex.Message}"); } catch { } }
        }
    }
}
