using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Verse;

namespace KMHPatch.Features.Rwt
{
    // Read from GitHub's release metadata, not scraped; a server's advertised version is never substituted.
    internal static class RwtReleaseCatalog
    {
        public const string Owner = "RimWorld-Together";
        public const string Repo  = "Rimworld-Together";

        private const int MaxReleases = 30;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

        public enum CatalogState { Unknown, Fetching, Ready, Unavailable }

        public static CatalogState State { get; private set; } = CatalogState.Unknown;
        public static string LastError { get; private set; } = "";

        private static readonly object _lock = new object();
        private static List<RwtRelease> _releases = new List<RwtRelease>();
        private static DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

        internal static string RootOverrideForTest;

        private static string CacheRoot => RootOverrideForTest
            ?? Persistence.KmhDataFolder.Root;

        private static string CachePath => Path.Combine(CacheRoot, "rwt-releases.json");

        private static readonly HttpClient Http = Global.KmhHttps.Client(TimeSpan.FromSeconds(25));

        public const int RecentStable = 10;

        // Old means not among the newest stable releases the catalog knows; an unknown catalog calls nothing old.
        public static bool IsOld(RwtRelease rel)
        {
            if (rel == null) return false;
            List<RwtRelease> stable;
            lock (_lock) stable = _releases.Where(r => r.Stable).ToList();
            if (stable.Count == 0) return false;
            int at = stable.FindIndex(r => RwtVersionText.Same(r.Version, rel.Version));
            return at < 0 ? RwtVersionText.Compare(rel.Version, stable[Math.Min(RecentStable, stable.Count) - 1].Version) < 0 : at >= RecentStable;
        }

        public static IReadOnlyList<RwtRelease> Releases
        {
            get { lock (_lock) return _releases.ToList(); }
        }

        // Never guessed: with no metadata this is null rather than whatever version was compiled in.
        public static RwtRelease LatestStable
        {
            get
            {
                lock (_lock)
                    return _releases.Where(r => r.Stable)
                                    .OrderByDescending(r => r.Version, Comparer<string>.Create(RwtVersionText.Compare))
                                    .FirstOrDefault();
            }
        }

        public static RwtRelease Find(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;
            lock (_lock) return _releases.FirstOrDefault(r => RwtVersionText.Same(r.Version, version));
        }

        // The browser calls EnsureLoaded every frame, so a failed refresh waits this long or GitHub gets hammered.
        internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);
        private static DateTimeOffset _failedAt = DateTimeOffset.MinValue;

        public static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (State == CatalogState.Fetching) return;
                if (State == CatalogState.Ready && DateTimeOffset.UtcNow - _fetchedAt < CacheLifetime) return;
                if (DateTimeOffset.UtcNow - _failedAt < RetryAfterFailure) return;
                if (_releases.Count == 0) LoadCache();
                State = CatalogState.Fetching;
            }

            Task.Run(async () =>
            {
                try { await RefreshAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    LastError = ex.GetType().Name;
                    KmhLog.Debug($"RWT catalog: refresh failed: {ex.GetType().Name}");
                    lock (_lock) { State = _releases.Count > 0 ? CatalogState.Ready : CatalogState.Unavailable; _failedAt = DateTimeOffset.UtcNow; }
                }
                finally { lock (_lock) if (State != CatalogState.Ready || LastError.Length > 0) _failedAt = DateTimeOffset.UtcNow; }
            });
        }

        // A server pinned below the newest 30 is looked up by exact tag; a miss retries after RetryAfterFailure.
        private static readonly HashSet<string> _exactAsked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTimeOffset> _exactFailedAt = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        // True while an exact-tag lookup for this version is still running, so "no such release" is not said too early.
        public static bool ExactPending(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            lock (_lock) return _exactAsked.Contains(version.Trim()) && Find(version) == null && !_exactFailedAt.ContainsKey(version.Trim());
        }

        public static void EnsureExact(string version)
        {
            if (string.IsNullOrWhiteSpace(version) || Find(version) != null) return;
            string v = version.Trim();
            lock (_lock)
            {
                if (State != CatalogState.Ready) return;
                if (_exactFailedAt.TryGetValue(v, out DateTimeOffset failed))
                {
                    if (DateTimeOffset.UtcNow - failed < RetryAfterFailure) return;
                    _exactFailedAt.Remove(v);
                    _exactAsked.Remove(v);
                }
                if (!_exactAsked.Add(v)) return;
            }
            Task.Run(async () =>
            {
                try { if (await LookUpExactAsync(version).ConfigureAwait(false)) return; }
                catch (Exception ex) { KmhLog.Debug($"RWT catalog: exact lookup of {v} failed: {ex.GetType().Name}"); }
                lock (_lock) _exactFailedAt[v] = DateTimeOffset.UtcNow;
            });
        }

        private static async Task<bool> LookUpExactAsync(string version)
        {
            foreach (string tag in new[] { version.Trim(), "v" + version.Trim() })
            {
                try
                {
                    using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get,
                        $"https://api.github.com/repos/{Owner}/{Repo}/releases/tags/{Uri.EscapeDataString(tag)}");
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                    req.Headers.UserAgent.ParseAdd("ColonyNexus");
                    using HttpResponseMessage res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) continue;
                    string body = await ReadBoundedAsync(res, MaxCatalogBytes, CancellationToken.None).ConfigureAwait(false);
                    List<RwtRelease> one = body == null ? null : Parse("[" + body + "]");
                    if (one == null || one.Count == 0 || !RwtVersionText.Same(one[0].Version, version)) continue;
                    lock (_lock) if (!_releases.Any(r => RwtVersionText.Same(r.Version, one[0].Version))) _releases.Add(one[0]);
                    return true;
                }
                catch (Exception ex) { KmhLog.Debug($"RWT catalog: exact lookup of {tag} failed: {ex.GetType().Name}"); }
            }
            return false;
        }

        public static async Task RefreshAsync(CancellationToken ct)
        {
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page={MaxReleases}");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            req.Headers.UserAgent.ParseAdd("ColonyNexus");

            using HttpResponseMessage res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) { RefreshFailed($"HTTP {(int)res.StatusCode}"); return; }

            string body = await ReadBoundedAsync(res, MaxCatalogBytes, ct).ConfigureAwait(false);
            if (body == null) { RefreshFailed("release list too large"); return; }
            List<RwtRelease> parsed = Parse(body);
            if (parsed.Count == 0) { RefreshFailed("no releases parsed"); return; }

            lock (_lock)
            {
                // Releases found by exact tag are older than the newest 30, so a refresh keeps them.
                foreach (RwtRelease kept in _releases)
                    if (!parsed.Any(r => RwtVersionText.Same(r.Version, kept.Version))) parsed.Add(kept);
                _releases = parsed;
                _fetchedAt = DateTimeOffset.UtcNow;
                State = CatalogState.Ready;
                LastError = "";
            }
            SaveCache(body);
        }

        // A list already held stays usable after one failed refresh.
        private static void RefreshFailed(string error)
        {
            LastError = error;
            lock (_lock) State = _releases.Count > 0 ? CatalogState.Ready : CatalogState.Unavailable;
        }

        // Thirty releases are a few hundred KB; anything near this is not GitHub's release list.
        internal const int MaxCatalogBytes = 4 * 1024 * 1024;

        // Null when the body runs past maxBytes, declared or not.
        internal static async Task<string> ReadBoundedAsync(HttpResponseMessage res, int maxBytes, CancellationToken ct)
        {
            if (res.Content.Headers.ContentLength > maxBytes) return null;
            using (Stream s = await res.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[16384];
                int n;
                while ((n = await s.ReadAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + n > maxBytes) return null;
                    buffer.Write(chunk, 0, n);
                }
                return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
        }

        public static List<RwtRelease> Parse(string json)
        {
            List<RwtRelease> list = new List<RwtRelease>();
            JArray arr;
            try { arr = JArray.Parse(json); } catch (Exception) { return list; }

            foreach (JToken t in arr)
            {
                if (t is not JObject o) continue;
                bool draft = (bool?)o["draft"] ?? false;
                if (draft) continue;   // a draft is not published, so it is not installable

                RwtRelease rel = new RwtRelease
                {
                    Tag = (string)o["tag_name"] ?? "",
                    Prerelease = (bool?)o["prerelease"] ?? false,
                    Draft = false,
                };
                rel.Version = RwtVersionText.Normalize(rel.Tag);
                if (rel.Version.Length == 0) continue;

                if (DateTimeOffset.TryParse((string)o["published_at"], out DateTimeOffset pub)) rel.PublishedAt = pub;

                if (o["assets"] is JArray assets)
                {
                    foreach (JToken at in assets)
                    {
                        if (at is not JObject ao) continue;
                        RwtAsset a = new RwtAsset
                        {
                            Name = (string)ao["name"] ?? "",
                            Url = (string)ao["browser_download_url"] ?? "",
                            Size = (long?)ao["size"] ?? 0,
                            Sha256 = ReadDigest((string)ao["digest"]),
                        };
                        // An asset that would download from anywhere but GitHub's release hosts is not offered at all.
                        if (a.Name.Length == 0 || !RwtPackage.IsReleaseDownloadUrl(a.Url)) continue;
                        RwtAssetClassifier.Classify(a);
                        rel.Assets.Add(a);
                    }
                }

                if (rel.Assets.Count > 0) list.Add(rel);
            }

            // Two tags can normalise to one version when a release is re-cut; the newer publication wins.
            return list.GroupBy(r => r.Version, StringComparer.OrdinalIgnoreCase)
                       .Select(g => g.OrderByDescending(r => r.PublishedAt).First())
                       .OrderByDescending(r => r.Version, Comparer<string>.Create(RwtVersionText.Compare))
                       .ToList();
        }

        public static string ReadDigest(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim();
            const string prefix = "sha256:";
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) s = s.Substring(prefix.Length);
            return s.Length == 64 && s.All(Uri.IsHexDigit) ? s.ToLowerInvariant() : "";
        }

        private static void LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                List<RwtRelease> cached = Parse(File.ReadAllText(CachePath));
                if (cached.Count == 0) return;
                _releases = cached;
                State = CatalogState.Ready;
            }
            catch (Exception ex) { KmhLog.Debug($"RWT catalog: cache unreadable: {ex.Message}"); }
        }

        private static void SaveCache(string body)
        {
            try
            {
                Directory.CreateDirectory(CacheRoot);
                File.WriteAllText(CachePath, body);
            }
            catch (Exception ex) { KmhLog.Debug($"RWT catalog: cache not written: {ex.Message}"); }
        }

        internal static void SeedForTest(List<RwtRelease> releases)
        {
            lock (_lock)
            {
                _releases = releases ?? new List<RwtRelease>();
                _fetchedAt = DateTimeOffset.UtcNow;
                State = _releases.Count > 0 ? CatalogState.Ready : CatalogState.Unavailable;
            }
        }

        internal static void ResetForTest()
        {
            lock (_lock)
            {
                _releases = new List<RwtRelease>();
                _fetchedAt = DateTimeOffset.MinValue;
                State = CatalogState.Unknown;
                LastError = "";
                _exactAsked.Clear();
                _exactFailedAt.Clear();
            }
        }

        internal static void FailExactForTest(string version, DateTimeOffset at) { lock (_lock) { _exactAsked.Add(version); _exactFailedAt[version] = at; } }
    }
}
