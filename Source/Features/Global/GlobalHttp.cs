using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Global
{
    internal static class GlobalHttp
    {
        public sealed class Result
        {
            public bool    Ok;
            public int     Status;
            public string  ErrorCode = "";
            public string  CorrelationId = "";
            public JObject Body;
            // Non-zero only when the server named a delay.
            public TimeSpan RetryAfter;

            // A 429 is the directory answering, not down; conflating them showed Global offline while it was online.
            public bool RateLimited => Status == 429;
            // Status 0 means nothing answered at all.
            public bool Unreachable => Status == 0 || Status >= 500;
        }

        private static readonly HttpClient Http = KmhHttps.Client(TimeSpan.FromSeconds(12));

        // A 1 MB report does not finish uploading in 12 s on a slow uplink.
        private static readonly HttpClient UploadHttp = KmhHttps.Client(TimeSpan.FromSeconds(90));

        // Bounded so a hostile or broken endpoint cannot stream the client out of memory.
        internal const int MaxResponseBytes = 2 * 1024 * 1024;

        internal static TimeSpan BodyTimeout = TimeSpan.FromSeconds(12);

        public const string OfficialBaseUrl = "https://global.knappe0.games";

#if KMH_PLAYTEST_DIRECTORY
        // Playtest packages (-p:KmhDirectory=preview) default to the staging directory.
        public const string DefaultBaseUrl = "https://preview.knappe0.games";
#else
        public const string DefaultBaseUrl = OfficialBaseUrl;
#endif

        public static string BaseUrl = DefaultBaseUrl;

        // A url with no scheme would become a relative path in HttpClient and never leave the process.
        public static string Normalize(string raw)
        {
            string s = (raw ?? "").Trim().TrimEnd('/');
            if (s.Length == 0) return DefaultBaseUrl;
            if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return s;
            // Bearer tokens are long-lived, so plain http is allowed only to loopback; elsewhere it would go in clear.
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return IsLoopback(s) ? s : DefaultBaseUrl;
            return DefaultBaseUrl;
        }

        public static bool IsPlaytestBuild => DefaultBaseUrl != OfficialBaseUrl;

        internal const string PlaytestBaseUrl = "https://preview.knappe0.games";

        // A playtester who typed the staging address would otherwise stay on it after updating to the release.
        internal static string ForRelease(string saved, bool playtestBuild)
            => !playtestBuild && string.Equals(Normalize(saved), PlaytestBaseUrl, StringComparison.OrdinalIgnoreCase) ? "" : saved;

        internal static bool IsLoopback(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri u)) return false;
            if (u.IsLoopback) return true;
            string h = u.Host;
            return h.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || h.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOfficial => BaseUrl == OfficialBaseUrl;

        // Colony Nexus's own directory: the official one, or a playtest build's own staging one. Only it can mark official.
        public static bool IsFirstParty => IsOfficial || (IsPlaytestBuild && BaseUrl == DefaultBaseUrl);

        public static Task<Result> GetAsync(string path, CancellationToken ct) => GetAsync(path, null, ct);

        public static Task<Result> GetAsync(string path, string bearer, CancellationToken ct)
            => SendAsync(HttpMethod.Get, path, null, bearer, ct);

        public static Task<Result> PostAsync(string path, string json, string bearer, CancellationToken ct)
            => SendAsync(HttpMethod.Post, path, json, bearer, ct);

        public static Task<Result> DeleteAsync(string path, string bearer, CancellationToken ct)
            => SendAsync(HttpMethod.Delete, path, null, bearer, ct);

        public static Task<Result> PostUploadAsync(string path, string json, string bearer, CancellationToken ct)
            => SendAsync(HttpMethod.Post, path, json, bearer, ct, upload: true);

        // In-game self-tests by a linked player once posted to live Global Chat; this is the one exit to Global.
        internal static bool SuppressForTest;

        internal static int SuppressedCallCount;

        internal static Func<HttpRequestMessage, HttpResponseMessage> RespondForTest;

        // This client's own refusal of a stale answer, never something Global sends.
        internal const string IdentityChangedCode = "KMH-CLI-IDENTITY-CHANGED";

        private static async Task<Result> SendAsync(HttpMethod method, string path, string json, string bearer,
            CancellationToken ct, bool upload = false)
        {
            Func<HttpRequestMessage, HttpResponseMessage> responder = SuppressForTest ? RespondForTest : null;
            if (SuppressForTest && responder == null)
            {
                SuppressedCallCount++;
                KmhLog.Debug($"Self-test: {method} {path} was not sent - the suite never talks to Global.");
                return new Result { Ok = false, Status = 0, ErrorCode = "KMH-TEST-SUPPRESSED" };
            }

            // Captured at the single exit point so a later-added caller cannot forget the stale-identity check.
            int identity = string.IsNullOrEmpty(bearer) ? -1 : KmhIdentityGeneration.Current;

            try
            {
                using HttpRequestMessage req = new HttpRequestMessage(method, BaseUrl.TrimEnd('/') + path);
                if (json != null) req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(bearer))
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

                // Headers only: the default buffers the whole body before any length can be checked.
                using HttpResponseMessage res = responder != null
                    ? responder(req)
                    : await (upload ? UploadHttp : Http).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                // Identity changed in flight: 409, not 0, so it neither reads as Global down nor starts a backoff.
                if (identity >= 0 && !KmhIdentityGeneration.IsCurrent(identity))
                    return new Result { Ok = false, Status = 409, ErrorCode = IdentityChangedCode };

                // The client timeout stops at the headers once they are read, so the body gets its own.
                string text;
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    deadline.CancelAfter(BodyTimeout);
                    using (deadline.Token.Register(() => { try { res.Dispose(); } catch (Exception) { } }))
                        text = await ReadBoundedAsync(res.Content, MaxResponseBytes, deadline.Token).ConfigureAwait(false);
                }
                if (text == null) return new Result { Status = (int)res.StatusCode, ErrorCode = "KMH-SEC-502" };

                Result r = new Result { Status = (int)res.StatusCode, Ok = res.IsSuccessStatusCode };
                // Retry-After beats the client's own backoff, or it hammers a directory that said when to return.
                if (res.Headers.RetryAfter?.Delta is TimeSpan d) r.RetryAfter = d;
                else if (res.Headers.RetryAfter?.Date is DateTimeOffset at)
                    r.RetryAfter = at - DateTimeOffset.UtcNow;
                if (res.Headers.TryGetValues("X-KMH-Correlation-Id", out var ids))
                    foreach (string id in ids) { r.CorrelationId = id; break; }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        r.Body = JObject.Parse(text);
                        JToken err = r.Body["error"];
                        if (err != null)
                        {
                            r.ErrorCode = (string)err["code"] ?? "";
                            if (string.IsNullOrEmpty(r.CorrelationId)) r.CorrelationId = (string)err["correlation_id"] ?? "";
                        }
                    }
                    catch (Exception) { r.Ok = false; r.ErrorCode = "KMH-SEC-503"; }
                }
                return r;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Never logged with the url's query: a join ticket could be in it.
                KmhLog.Debug($"Global: request to {path} did not complete: {ex.GetType().Name}");
                return new Result { Status = 0, ErrorCode = "KMH-NET-001" };
            }
        }

        // Null past max; stops reading there whatever Content-Length claimed.
        internal static async Task<string> ReadBoundedAsync(HttpContent content, int max, CancellationToken ct)
        {
            if (content == null) return "";
            if (content.Headers.ContentLength > max) return null;
            using (System.IO.Stream body = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (System.IO.MemoryStream into = new System.IO.MemoryStream())
            {
                byte[] buffer = new byte[16 * 1024];
                int n;
                while ((n = await body.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    if (into.Length + n > max) return null;
                    into.Write(buffer, 0, n);
                }
                // A stream closed by the deadline can read as a clean end; what arrived by then is not the answer.
                ct.ThrowIfCancellationRequested();
                into.Position = 0;
                using (System.IO.StreamReader reader = new System.IO.StreamReader(into, new System.Text.UTF8Encoding(false), true))
                    return reader.ReadToEnd();
            }
        }
    }
}
