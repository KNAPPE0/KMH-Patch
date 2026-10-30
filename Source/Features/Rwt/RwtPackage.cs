using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KMHPatch.Features.Rwt
{
    // All network bytes are bounded here; each refusal is a player-readable sentence and null means it went through.
    internal static class RwtPackage
    {
        // The official client package is tens of MB; these bound a broken or hostile response, not a real release.
        public const long MaxPackageBytes = 256L * 1024 * 1024;
        public const int MaxEntries = 20_000;
        public const long MaxUnpackedBytes = 1024L * 1024 * 1024;
        public const int MaxRedirects = 5;
        public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(15);

        // GitHub's release download and the two hosts it redirects that download to.
        private static readonly string[] OfficialHosts = { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };

        public static bool IsOfficialUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out Uri u) && IsOfficial(u);

        // The first hop must be a release asset of the RWT repository itself, not any github.com address.
        private static readonly string ReleasePath = $"/{RwtReleaseCatalog.Owner}/{RwtReleaseCatalog.Repo}/releases/download/";

        public static bool IsReleaseDownloadUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out Uri u) && IsOfficial(u)
            && string.Equals(u.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && u.AbsolutePath.StartsWith(ReleasePath, StringComparison.OrdinalIgnoreCase)
            && u.AbsolutePath.IndexOf("/../", StringComparison.Ordinal) < 0 && string.IsNullOrEmpty(u.Query);

        public static bool IsOfficial(Uri u)
        {
            if (u == null || u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort || !string.IsNullOrEmpty(u.UserInfo)) return false;
            foreach (string h in OfficialHosts)
                if (string.Equals(u.Host, h, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static readonly HttpClient Http = Global.KmhHttps.Client(Timeout.InfiniteTimeSpan, redirects: false);

        // Redirects are followed by hand so every hop is checked against `allowed`; the body is capped by the declared
        // length, the expected size and maxBytes, and a read that stalls or a download that runs too long is abandoned.
        public static async Task<string> DownloadAsync(string url, string destination, long expectedBytes, long maxBytes,
                                                       Func<Uri, bool> allowed, TimeSpan stall, TimeSpan total, CancellationToken ct)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri at) || !allowed(at)) return "the download address is not an official release address";
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(total);
                for (int hop = 0; ; hop++)
                {
                    HttpResponseMessage res;
                    using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, at))
                    {
                        req.Headers.UserAgent.ParseAdd("ColonyNexus");
                        try { res = await WithinAsync(Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, deadline.Token), stall, deadline.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return "the download did not answer in time"; }
                        catch (HttpRequestException ex) { return "the download could not connect: " + (ex.InnerException?.Message ?? ex.Message); }
                    }
                    if (res == null) return "the download did not answer in time";
                    using (res)
                    {
                        int code = (int)res.StatusCode;
                        if (code >= 300 && code < 400)
                        {
                            Uri next = res.Headers.Location == null ? null
                                : res.Headers.Location.IsAbsoluteUri ? res.Headers.Location : new Uri(at, res.Headers.Location);
                            if (next == null) return $"the download redirected nowhere (HTTP {code})";
                            if (!allowed(next)) return "the download redirected away from the official release hosts";
                            if (hop + 1 >= MaxRedirects) return "the download redirected too many times";
                            at = next;
                            continue;
                        }
                        if (!res.IsSuccessStatusCode) return $"the download returned HTTP {code}";

                        long cap = expectedBytes > 0 ? Math.Min(expectedBytes, maxBytes) : maxBytes;
                        long? declared = res.Content.Headers.ContentLength;
                        if (declared > cap) return $"the download is larger than the release says ({declared} bytes)";
                        if (expectedBytes > 0 && declared.HasValue && declared != expectedBytes) return "the download is not the size the release lists";

                        string partial = destination + ".partial";
                        try
                        {
                            long written = 0;
                            using (Stream body = await WithinAsync(res.Content.ReadAsStreamAsync(), stall, deadline.Token).ConfigureAwait(false))
                            using (FileStream fs = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                if (body == null) return "the download did not answer in time";
                                byte[] buffer = new byte[81920];
                                while (true)
                                {
                                    Task<int> read = body.ReadAsync(buffer, 0, buffer.Length, deadline.Token);
                                    if (await Task.WhenAny(read, Task.Delay(stall, deadline.Token)).ConfigureAwait(false) != read)
                                    {
                                        res.Dispose();   // a stream that ignores the token still stops when its response goes
                                        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                                        return deadline.IsCancellationRequested ? "the download took too long" : "the download stalled";
                                    }
                                    int n = await read.ConfigureAwait(false);
                                    if (n <= 0) break;
                                    written += n;
                                    if (written > cap) return "the download is larger than the release says";
                                    fs.Write(buffer, 0, n);
                                }
                            }
                            if (expectedBytes > 0 && written != expectedBytes) return "the download ended early";
                            Persistence.KmhAtomicFile.Promote(partial, destination);
                            return null;
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return "the download took too long"; }
                        catch (IOException ex) { return "the download could not be stored: " + ex.Message; }
                        finally { try { if (File.Exists(partial)) File.Delete(partial); } catch (Exception) { } }
                    }
                }
            }
        }

        private static async Task<T> WithinAsync<T>(Task<T> work, TimeSpan limit, CancellationToken ct) where T : class
        {
            Task first = await Task.WhenAny(work, Task.Delay(limit, ct)).ConfigureAwait(false);
            if (first != work)
            {
                ct.ThrowIfCancellationRequested();
                _ = work.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) (t.Result as IDisposable)?.Dispose(); },
                                      TaskScheduler.Default);
                return null;
            }
            return await work.ConfigureAwait(false);
        }

        // Every entry is checked before a byte of it is written: inside staging, no drive or stream names, no links, and the
        // archive as a whole bounded in entries and in what it unpacks to (counted as written, not as declared).
        public static string Extract(string archive, string staging, int maxEntries = MaxEntries, long maxUnpacked = MaxUnpackedBytes)
        {
            string root = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(archive))
                {
                    if (zip.Entries.Count > maxEntries) return $"the package has too many files ({zip.Entries.Count})";
                    long unpacked = 0;
                    byte[] buffer = new byte[81920];
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        string target = SafeEntryPath(root, e.FullName, out string refusal);
                        if (target == null) return refusal;
                        if (IsLink(e)) return $"the package contains a link ({e.FullName})";
                        if (e.FullName.EndsWith("/", StringComparison.Ordinal) || e.FullName.EndsWith("\\", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (Stream src = e.Open())
                        using (FileStream dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            int n;
                            while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                unpacked += n;
                                if (unpacked > maxUnpacked) return "the package unpacks to more than any release could";
                                dst.Write(buffer, 0, n);
                            }
                        }
                    }
                }
                return null;
            }
            catch (InvalidDataException ex) { return "the package is not a readable zip: " + ex.Message; }
            catch (IOException ex) { return "the package could not be unpacked: " + ex.Message; }
            catch (UnauthorizedAccessException ex) { return "the package could not be unpacked: " + ex.Message; }
        }

        internal static string SafeEntryPath(string root, string name, out string refusal)
        {
            refusal = null;
            string n = (name ?? "").Replace('\\', '/');
            if (n.Length == 0 || n.Length > 400) { refusal = "the package contains an unusable file name"; return null; }
            if (n.StartsWith("/", StringComparison.Ordinal) || (n.Length > 1 && n[1] == ':') || n.IndexOf(':') >= 0)
            { refusal = $"the package names a location outside itself ({name})"; return null; }
            string trimmed = n.EndsWith("/", StringComparison.Ordinal) ? n.Substring(0, n.Length - 1) : n;
            foreach (string part in trimmed.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..")
                { refusal = $"the package names a location outside itself ({name})"; return null; }
                if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.EndsWith(".", StringComparison.Ordinal)
                    || part.EndsWith(" ", StringComparison.Ordinal) || IsDeviceName(part))
                { refusal = $"the package contains an unusable file name ({name})"; return null; }
            }
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, n.TrimEnd('/'))); }
            catch (Exception) { refusal = $"the package contains an unusable file name ({name})"; return null; }
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { refusal = $"the package names a location outside itself ({name})"; return null; }
            return full;
        }

        // On Windows "CON" or "nul.txt" opens a device rather than a file, whatever folder it is in.
        private static bool IsDeviceName(string part)
        {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL") return true;
            return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3] >= '1' && stem[3] <= '9';
        }

        // Unix mode bits ride in the top half of the external attributes; S_IFLNK is a symbolic link. Read by reflection,
        // because the property is missing on some runtimes the game ships; without it nothing here creates a link anyway.
        private static readonly System.Reflection.PropertyInfo External = typeof(ZipArchiveEntry).GetProperty("ExternalAttributes");

        private static bool IsLink(ZipArchiveEntry e)
        {
            if (External == null) return false;
            int attrs = (int)External.GetValue(e);
            return ((attrs >> 16) & 0xF000) == 0xA000;
        }
    }
}
