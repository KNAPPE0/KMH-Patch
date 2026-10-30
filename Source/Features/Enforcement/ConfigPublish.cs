using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using Newtonsoft.Json.Linq;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    internal enum PublishPhase { Idle, Preparing, Hashing, Waiting, Uploading, Finalizing, Done, Failed, Cancelled }

    // Runs off the main thread; success is only the server's committed result, never "last part handed off".
    internal static class ConfigPublish
    {
        public static PublishPhase Phase { get; private set; } = PublishPhase.Idle;
        public static string  Message { get; private set; } = "";
        public static int     FilesTotal { get; private set; }
        public static int     FilesDone { get; private set; }
        public static long    BytesTotal { get; private set; }
        public static long    BytesSent { get; private set; }
        public static long    BytesReused { get; private set; }
        public static long    CommittedRevision { get; private set; }
        public static string  CommittedHash { get; private set; } = "";

        public static bool Active => Phase is PublishPhase.Preparing or PublishPhase.Hashing
                                            or PublishPhase.Waiting or PublishPhase.Uploading or PublishPhase.Finalizing;

        public static float Progress => FilesTotal <= 0 ? 0f : Math.Min(1f, (float)FilesDone / FilesTotal);

        public static long ManifestMs { get; private set; }
        public static long TransferMs { get; private set; }
        public static long TotalMs { get; private set; }
        public static int  FramesSent { get; private set; }

        private static ConfigManifest _manifest;
        private static string _uploadId = "";
        private static string _configRoot = "";
        private static CancellationTokenSource _cts;
        private static readonly object _gate = new object();

        // The server's answers arrive on the network thread; the worker waits on these rather than polling.
        private static TaskCompletionSource<JObject> _accepted;
        private static TaskCompletionSource<JObject> _result;
        private static SemaphoreSlim _credit;

        public static void Cancel()
        {
            CancellationTokenSource cts;
            lock (_gate) cts = _cts;
            if (cts == null) return;
            Set(PublishPhase.Cancelled, "Cancelling…");
            try { cts.Cancel(); } catch { }
            if (!string.IsNullOrEmpty(_uploadId))
                KmhDispatcher.Send(KmhProtocol.Kind.EnforcementPublishCancel, new { upload_id = _uploadId });
        }

        // Reads game state here on the main thread; everything after runs on the worker.
        public static void Start()
        {
            lock (_gate)
            {
                if (Active) return;
                _cts = new CancellationTokenSource();
            }
            _configRoot = GenFilePaths.ConfigFolderPath;
            long expectedRevision = EnforcementCache.ProfileRevision;
            Reset();
            Set(PublishPhase.Preparing, "Preparing…");
            Task.Run(() => RunAsync(expectedRevision, _cts.Token));
        }

        private static void Reset()
        {
            FilesTotal = FilesDone = FramesSent = 0;
            BytesTotal = BytesSent = BytesReused = 0;
            CommittedRevision = 0; CommittedHash = ""; _uploadId = "";
            ManifestMs = TransferMs = TotalMs = 0;
            _accepted = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _result   = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task RunAsync(long expectedRevision, CancellationToken ct)
        {
            Stopwatch all = Stopwatch.StartNew();
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                Set(PublishPhase.Hashing, "Hashing configs…");
                ConfigManifest manifest = ConfigManifest.Build(_configRoot, EnforcementMods.IsSafeModConfigFile);
                ManifestMs = sw.ElapsedMilliseconds;
                if (manifest.Count == 0) { Fail("Nothing to publish - no managed config files were found."); return; }

                _manifest = manifest;
                FilesTotal = manifest.Count;
                BytesTotal = manifest.TotalBytes;
                ct.ThrowIfCancellationRequested();

                Set(PublishPhase.Waiting, $"Offering {manifest.Count} file(s)…");
                var files = new List<object>(manifest.Count);
                foreach (ConfigManifestEntry e in manifest.Entries)
                    files.Add(new { path = e.Path, size = e.Size, hash = e.Hash });

                if (!Send(KmhProtocol.Kind.EnforcementPublishBegin, new
                {
                    files,
                    profile_hash = manifest.Hash,
                    total_bytes = manifest.TotalBytes,
                    expected_revision = expectedRevision,
                }))
                { Fail("Not connected to a Colony Nexus server."); return; }

                JObject accepted = await WithTimeout(_accepted.Task, TimeSpan.FromSeconds(30), ct,
                                                     "The server did not answer the publish offer.").ConfigureAwait(false);
                if (accepted == null) return;

                _uploadId = (string)accepted["upload_id"] ?? "";
                // Validated because the send loop advances by it: 0 sent empty parts forever.
                if (!TryPartBytes(accepted["part_bytes"], out int partBytes))
                {
                    string asked = accepted["part_bytes"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "?";
                    Fail(UI.KmhText.Get("KMH_Publish_BadPartSize",
                        "The server asked for upload parts of {0} bytes, outside the {1} to {2} this client sends. Nothing was uploaded.",
                        UI.KmhDisplayText.Inert(asked, 24), MinPartBytes, MaxPartBytes));
                    return;
                }
                int credit    = Math.Max(1, (int?)accepted["credit"] ?? 8);
                BytesReused   = (long?)accepted["reused_bytes"] ?? 0;
                int[] wanted  = ReadIndices(accepted["wanted"]);
                _credit = new SemaphoreSlim(credit, credit);

                FilesDone = manifest.Count - wanted.Length;
                BytesSent = BytesReused;

                if (wanted.Length == 0)
                {
                    Set(PublishPhase.Finalizing, "Already up to date - committing…");
                }
                else
                {
                    Set(PublishPhase.Uploading, $"Uploading 0/{wanted.Length}…");
                    Stopwatch tx = Stopwatch.StartNew();
                    int done = 0;
                    foreach (int index in wanted)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (index < 0 || index >= manifest.Entries.Count) continue;
                        ConfigManifestEntry entry = manifest.Entries[index];
                        if (!await SendFileAsync(index, entry, partBytes, ct).ConfigureAwait(false)) return;
                        done++; FilesDone++;
                        Set(PublishPhase.Uploading, $"Uploading {done}/{wanted.Length}…");
                    }
                    TransferMs = tx.ElapsedMilliseconds;
                    Set(PublishPhase.Finalizing, "Finalizing…");
                }

                if (!Send(KmhProtocol.Kind.EnforcementPublishCommit, new { upload_id = _uploadId }))
                { Fail("The connection dropped before the profile could be committed."); return; }

                JObject result = await WithTimeout(_result.Task, TimeSpan.FromSeconds(60), ct,
                                                   "The server did not confirm the commit.").ConfigureAwait(false);
                if (result == null) return;

                if ((bool?)result["ok"] != true)
                {
                    Fail((string)result["message"] ?? $"The server refused the publish ({(string)result["error"]}).");
                    return;
                }

                CommittedRevision = (long?)result["revision"] ?? 0;
                CommittedHash     = (string)result["hash"] ?? "";
                TotalMs = all.ElapsedMilliseconds;
                int reusedFiles = (int?)result["reused_files"] ?? 0;
                Set(PublishPhase.Done,
                    $"Published revision {CommittedRevision} - {(int?)result["files"] ?? FilesTotal} file(s)" +
                    (reusedFiles > 0 ? $" ({reusedFiles} unchanged)" : "") +
                    $", {((long?)result["bytes"] ?? 0) / 1024}KB, hash {ShortHash(CommittedHash)}.");
                KmhLog.Info($"Config publish: rev {CommittedRevision}, {FilesTotal} file(s), " +
                            $"{BytesTotal / 1024}KB raw, {(BytesSent - BytesReused) / 1024}KB sent, " +
                            $"{BytesReused / 1024}KB reused, {FramesSent} frame(s), " +
                            $"manifest {ManifestMs}ms, transfer {TransferMs}ms, total {TotalMs}ms.");
            }
            catch (OperationCanceledException) { Set(PublishPhase.Cancelled, "Publish cancelled."); }
            catch (Exception ex) { Fail($"Publish failed: {ex.Message}"); }
            finally { lock (_gate) { _cts?.Dispose(); _cts = null; } }
        }

        // Deflated whole: the manifest caps a file at 8MB, so the buffer is bounded without a streaming inflater.
        private static async Task<bool> SendFileAsync(int index, ConfigManifestEntry entry, int partBytes, CancellationToken ct)
        {
            string full = Path.Combine(_configRoot, entry.Path);
            byte[] packed;
            try
            {
                byte[] raw = File.ReadAllBytes(full);
                using MemoryStream into = new MemoryStream();
                using (DeflateStream z = new DeflateStream(into, CompressionLevel.Optimal, leaveOpen: true))
                    z.Write(raw, 0, raw.Length);
                packed = into.ToArray();
            }
            catch (Exception ex) { Fail($"Could not read '{entry.Path}': {ex.Message}"); return false; }

            long sent = 0;
            for (int off = 0; off < packed.Length; off += partBytes)
            {
                ct.ThrowIfCancellationRequested();
                int take = Math.Min(partBytes, packed.Length - off);
                // Backpressure: never more parts in flight than the server granted credit for.
                await _credit.WaitAsync(ct).ConfigureAwait(false);
                sent += take;

                bool ok = Send(KmhProtocol.Kind.EnforcementPublishPart, new
                {
                    upload_id = _uploadId,
                    index,
                    data = Convert.ToBase64String(packed, off, take),
                    packed_size = packed.Length,
                    last = sent >= packed.Length,
                });
                FramesSent++;
                BytesSent += take;
                if (!ok) { Fail("The connection dropped during the upload."); return false; }
                if (_result.Task.IsCompleted) { await HandleEarlyResultAsync().ConfigureAwait(false); return false; }
            }
            return true;
        }

        // A refusal arrives as a result, not an ack, so stop rather than stream into a transaction already gone.
        private static async Task HandleEarlyResultAsync()
        {
            JObject r = await _result.Task.ConfigureAwait(false);
            Fail((string)r["message"] ?? "The server refused the publish.");
        }

        private static async Task<JObject> WithTimeout(Task<JObject> task, TimeSpan timeout, CancellationToken ct, string onTimeout)
        {
            Task done = await Task.WhenAny(task, Task.Delay(timeout, ct)).ConfigureAwait(false);
            if (done != task) { Fail(onTimeout); return null; }
            return await task.ConfigureAwait(false);
        }

        // The server refuses a part over 32KB, which is its own frame size; under 1KB is not a real answer.
        internal const int MinPartBytes = 1024, MaxPartBytes = 32 * 1024;

        // Absent means an older server, which always used the maximum.
        internal static bool TryPartBytes(JToken token, out int partBytes)
        {
            partBytes = MaxPartBytes;
            if (token == null || token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.Integer) return false;
            long v;
            try { v = (long)token; }
            catch (Exception) { return false; }
            if (v < MinPartBytes || v > MaxPartBytes) return false;
            partBytes = (int)v;
            return true;
        }

        internal static Func<string, object, bool> SendForTest;

        private static bool Send(string kind, object data)
            => SendForTest != null ? SendForTest(kind, data) : KmhDispatcher.Send(kind, data);

        internal static System.Threading.Tasks.Task RunForTest(string configRoot, CancellationToken ct)
        {
            _configRoot = configRoot;
            Reset();
            return RunAsync(0, ct);
        }

        private static int[] ReadIndices(JToken token)
        {
            if (!(token is JArray arr)) return Array.Empty<int>();
            var list = new List<int>(arr.Count);
            foreach (JToken t in arr) { int? v = (int?)t; if (v.HasValue) list.Add(v.Value); }
            return list.ToArray();
        }

        public static void OnAccepted(JObject body) => _accepted?.TrySetResult(body);
        public static void OnResult(JObject body)   => _result?.TrySetResult(body);

        public static void OnAck(JObject body)
        {
            try { _credit?.Release(); } catch (SemaphoreFullException) { }
        }

        // A disconnect mid-publish must not leave the window claiming it is still uploading.
        public static void OnDisconnected()
        {
            if (Active) Fail("Disconnected before the profile was committed.");
        }

        private static void Fail(string why) => Set(PublishPhase.Failed, why);

        private static void Set(PublishPhase phase, string message)
        {
            Phase = phase;
            Message = message ?? "";
        }

        internal static string ShortHash(string hash)
            => string.IsNullOrEmpty(hash) ? "?" : hash.Substring(0, Math.Min(8, hash.Length));
    }
}
