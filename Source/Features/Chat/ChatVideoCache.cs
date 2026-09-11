using System;
using System.Collections.Generic;
using System.IO;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Chat
{
    // Pulled down once and kept: a repeat or a loop then costs the server nothing and cannot stutter.
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
            internal long Read, Total;
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

        // Not UnityWebRequest: this Unity build refuses a plain-http url outright, and the relay speaks http.
        public static Download Begin(string streamUrl, string videoId, int height)
        {
            var d = new Download { File = PathFor(videoId, height) };
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
                req.Timeout = 30000;
                req.ReadWriteTimeout = 30000;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (Stream net = resp.GetResponseStream())
                using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    System.Threading.Interlocked.Exchange(ref d.Total, resp.ContentLength);
                    byte[] buf = new byte[64 * 1024];
                    int n;
                    while (net != null && (n = net.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (d.Cancelled) break;
                        file.Write(buf, 0, n);
                        System.Threading.Interlocked.Add(ref d.Read, n);
                    }
                }
            }
            catch (Exception ex) { d.WorkerError = ex.Message; }
            finally
            {
                // The stream is closed by here, so this is the one place a cancelled part-file can actually be removed.
                if (d.Cancelled) { try { File.Delete(part); } catch { } }
                d.WorkerDone = true;
            }
        }

        // Polled from the same pump as everything else; true once the download has finished, one way or the other.
        public static bool Poll(Download d)
        {
            if (d == null || d.Done) return true;

            long  read  = System.Threading.Interlocked.Read(ref d.Read);
            long  total = System.Threading.Interlocked.Read(ref d.Total);
            float now   = UnityEngine.Time.realtimeSinceStartup;

            float progress = total > 0 ? Math.Min(1f, (float)((double)read / total)) : 0f;
            if (progress > d.Progress || d.MovedAt <= 0f) d.MovedAt = now;
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
                string err = d.Cancelled ? "the download stopped part way" : d.WorkerError;
                if (!string.IsNullOrEmpty(err))
                {
                    d.Error = err;
                    try { if (File.Exists(part)) File.Delete(part); } catch { }
                }
                else
                {
                    try { if (File.Exists(d.File)) File.Delete(d.File); } catch { }
                    File.Move(part, d.File);
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

        // Age first, then size - and at startup too, or a cache left by an old session sits there for a fortnight.
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
