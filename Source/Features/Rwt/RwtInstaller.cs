using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Rwt
{
    internal enum RwtInstallStage
    {
        Idle, Downloading, Verifying, Extracting, Activating, Done, Failed,
    }

    // Every step happens beside the live copy; the working install is replaced only once a verified tree exists.
    internal static class RwtInstaller
    {
        public static RwtInstallStage Stage { get; private set; } = RwtInstallStage.Idle;
        public static string Detail { get; private set; } = "";
        public static string ActivatedVersion { get; private set; } = "";
        // False when GitHub published no checksum for the package and the player chose to install it anyway.
        public static bool LastVerified { get; private set; }

        internal static void SeedActivatedForTest(string version) => ActivatedVersion = version ?? "";

        internal static void NoteStaged(string version) => ActivatedVersion = version ?? "";

        private static int _running;
        private static CancellationTokenSource _cancel;

        internal static string ModsRootOverrideForTest;

        // RimWorld's own local Mods folder, the one it loads; Steam owns anything under workshop content.
        internal static string ModsRoot => ModsRootOverrideForTest ?? GenFilePaths.ModsFolderPath;

        internal static bool ForceCopyForTest;

        private static string CacheRoot => Path.Combine(
            RwtPaths.KmhRoot, "rwt-cache");

        public static bool Busy => Volatile.Read(ref _running) != 0;

        internal static void SetRunningForTest(bool running, RwtInstallStage stage, string detail)
        {
            Volatile.Write(ref _running, running ? 1 : 0);
            Stage = stage;
            Detail = detail ?? "";
        }

        // A package GitHub published no checksum for is only installed when the player said yes knowing that.
        internal static string Refusal(RwtInstallPlan plan, bool acceptUnverified)
        {
            if (plan == null || !plan.CanInstall) return "there is nothing to install";
            if (!RwtPackage.IsReleaseDownloadUrl(plan.Asset.Url)) return "the package is not on the official release hosts";
            if (!plan.Verifiable && !acceptUnverified) return "GitHub published no checksum for this package, and installing it unverified was not confirmed";
            if (plan.Unusual && !acceptUnverified) return "this is a pre-release or an old release, and installing it was not confirmed";
            return null;
        }

        public static void Begin(RwtInstallPlan plan, bool acceptUnverified = false)
        {
            // Busy first: a refused second request marked the running download Failed, and Prepare and Join reads that.
            if (Busy)
            {
                Messages.Message("A RimWorld Together download is already running.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            string refused = Refusal(plan, acceptUnverified);
            if (refused != null) { Fail(refused); return; }
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                Messages.Message("A RimWorld Together download is already running.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Stage = RwtInstallStage.Downloading;
            Detail = plan.Release.Version;
            CancellationTokenSource cancel = new CancellationTokenSource();
            Interlocked.Exchange(ref _cancel, cancel)?.Dispose();
            Task.Run(async () =>
            {
                try
                {
                    await RunAsync(plan, cancel.Token).ConfigureAwait(false);
                }
                // A cancel is the player's choice, not a failure to retry.
                catch (OperationCanceledException) { Stage = RwtInstallStage.Idle; Detail = ""; KmhLog.Info("RWT install cancelled."); }
                catch (Exception ex)
                {
                    Fail($"{ex.GetType().Name}: {ex.Message}");
                }
                finally { Volatile.Write(ref _running, 0); }
            });
        }

        public static void ResetFailure()
        {
            if (Busy || Stage != RwtInstallStage.Failed) return;
            Stage = RwtInstallStage.Idle;
            Detail = "";
        }

        public static void Cancel()
        {
            try { Volatile.Read(ref _cancel)?.Cancel(); } catch (ObjectDisposedException) { }
        }

        private static void Fail(string why)
        {
            Stage = RwtInstallStage.Failed;
            Detail = why;
            KmhLog.Warn($"RWT install failed: {why}");
        }

        private static async Task RunAsync(RwtInstallPlan plan, CancellationToken ct)
        {
            Directory.CreateDirectory(CacheRoot);
            string archive = Path.Combine(CacheRoot, $"{plan.Release.Version}-{Path.GetFileName(plan.Asset.Name)}");

            // Only a verified copy is reused: without a published checksum nothing proves the cached file is the release.
            if (!plan.Verifiable || !File.Exists(archive) || !DigestMatches(archive, plan.Asset.Sha256))
            {
                Stage = RwtInstallStage.Downloading;
                string failed = await RwtPackage.DownloadAsync(plan.Asset.Url, archive, plan.Asset.Size, RwtPackage.MaxPackageBytes,
                    RwtPackage.IsOfficial, RwtPackage.StallTimeout, RwtPackage.TotalTimeout, ct).ConfigureAwait(false);
                if (failed != null) { Fail(failed); return; }

                Stage = RwtInstallStage.Verifying;
                if (plan.Verifiable && !DigestMatches(archive, plan.Asset.Sha256))
                {
                    try { File.Delete(archive); } catch (Exception) { }
                    Fail("the downloaded package did not match its published checksum");
                    return;
                }
            }
            LastVerified = plan.Verifiable;
            if (!plan.Verifiable) KmhLog.Warn($"RWT install: {plan.Asset.Name} has no published checksum; installing it unverified as the player chose.");

            Stage = RwtInstallStage.Extracting;
            // Unpacked beside Mods so activation is a same-drive rename; the cache's drive may not be the Steam library's.
            RwtCopies.SweepIncoming();
            string staging = RwtCopies.NewIncoming();
            try { Directory.CreateDirectory(staging); }
            catch (Exception ex)
            {
                KmhLog.Warn($"RWT install: cannot unpack beside the Mods folder ({ex.Message}); unpacking in the cache and copying.");
                staging = Path.Combine(CacheRoot, $".staging-{plan.Release.Version}-{Guid.NewGuid():N}".Substring(0, 40));
                Directory.CreateDirectory(staging);
            }
            string unpackFailed = RwtPackage.Extract(archive, staging);
            if (unpackFailed != null)
            {
                DeleteQuietly(staging);
                Fail(unpackFailed);
                return;
            }

            string payload = FindModRoot(staging);
            if (payload == null)
            {
                DeleteQuietly(staging);
                Fail("the package did not contain a RimWorld Together mod folder");
                return;
            }

            Stage = RwtInstallStage.Activating;
            if (!Activate(payload, plan.Release.Version, plan.Verifiable, out string why)) { DeleteQuietly(staging); Fail(why); return; }
            DeleteQuietly(staging);

            ActivatedVersion = plan.Release.Version;
            Stage = RwtInstallStage.Done;
            Detail = plan.Release.Version;
            KmhLog.Info($"RWT {plan.Release.Version} activated from {plan.Asset.Name}; restart RimWorld to load it.");
        }

        internal static string FindModRoot(string staging)
        {
            if (Directory.Exists(Path.Combine(staging, "About"))) return staging;
            foreach (string dir in Directory.GetDirectories(staging))
                if (Directory.Exists(Path.Combine(dir, "About"))) return dir;
            return null;
        }

        internal static string PreviousRoot => RwtCopies.PreviousRoot;
        internal const int KeepPrevious = 2;
        private const string LegacyParkedMark = ".kmh-previous-";
        private const string LegacyParkedPattern = LegacyParkedMark + "*";

        internal static bool Activate(string payload, string version, bool verified, out string why)
        {
            why = "";
            string target = RwtCopies.LocalCopy;
            string keep = RwtCopies.NewPreviousSlot();
            string incoming = null;
            try
            {
                Directory.CreateDirectory(ModsRoot);
                Directory.CreateDirectory(PreviousRoot);
                // Only a rename swaps copies safely, and a rename only works on Mods' own drive.
                if (ForceCopyForTest || !RwtCopies.OnModsDrive(payload))
                {
                    incoming = RwtCopies.NewIncoming();
                    CopyTree(payload, incoming);
                    payload = incoming;
                }
                if (!string.IsNullOrEmpty(version)) RwtCopies.WriteMarker(payload, version, verified);
                RwtCopies.RememberOriginal();
                // The previous copy is moved aside rather than deleted, so a failed swap is still recoverable.
                if (Directory.Exists(target))
                {
                    bool players = !RwtCopies.IsKmhCopy(target);
                    Directory.Move(target, keep);
                    if (players) RwtCopies.NoteParkedOriginal(keep);
                }
                Directory.Move(payload, target);
            }
            catch (Exception ex)
            {
                why = $"could not activate the new copy: {ex.Message}";
                try { if (!Directory.Exists(target) && Directory.Exists(keep)) Directory.Move(keep, target); }
                catch (Exception) { }
                if (incoming != null) DeleteQuietly(incoming);
                return false;
            }

            // Best effort from here: the new copy is live whatever happens to the old ones.
            ParkLegacyCopies();
            PrunePrevious(keep);
            return true;
        }

        // Migration: earlier installs parked the previous copy inside Mods, where it can shadow the live one.
        private static void ParkLegacyCopies()
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(ModsRoot, RwtAssetClassifier.ClientWorkshopId + LegacyParkedPattern))
                {
                    try { Directory.Move(dir, Path.Combine(PreviousRoot, Path.GetFileName(dir))); }
                    catch (Exception ex) { KmhLog.Warn($"RWT install: could not move '{dir}' out of Mods: {ex.Message}"); }
                }
            }
            catch (Exception ex) { KmhLog.Debug($"RWT install: could not look for parked copies: {ex.GetType().Name}"); }
        }

        private static void CopyTree(string from, string to)
        {
            string src = Path.GetFullPath(from).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Directory.CreateDirectory(to);
            foreach (string dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, dir.Substring(src.Length + 1)));
            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, file.Substring(src.Length + 1)), true);
        }

        // The copy just set aside and the player's own survive; past KeepPrevious only marked copies KMH installed are deleted.
        private static void PrunePrevious(string justKept)
        {
            try
            {
                string mine = Path.GetFullPath(justKept);
                string guard = RwtCopies.ProtectedSlot;
                string own = guard.Length > 0 ? Path.GetFullPath(guard) : "";
                var older = new System.Collections.Generic.List<DirectoryInfo>();
                foreach (DirectoryInfo d in new DirectoryInfo(PreviousRoot).GetDirectories())
                {
                    string full = Path.GetFullPath(d.FullName);
                    if (string.Equals(full, mine, StringComparison.OrdinalIgnoreCase) || string.Equals(full, own, StringComparison.OrdinalIgnoreCase)) continue;
                    // A parked copy without the marker may be the player's own, moved aside by an older release.
                    if (!RwtCopies.IsKmhCopy(full)) continue;
                    older.Add(d);
                }
                older.Sort((a, b) => b.CreationTimeUtc.CompareTo(a.CreationTimeUtc));
                int allowed = Directory.Exists(mine) ? KeepPrevious - 1 : KeepPrevious;
                for (int i = allowed; i < older.Count; i++) DeleteQuietly(older[i].FullName);
            }
            catch (Exception ex) { KmhLog.Debug($"RWT install: could not prune previous copies: {ex.GetType().Name}"); }
        }

        // No checksum is no match: a missing digest never stands in for a verified one.
        internal static bool DigestMatches(string path, string sha256)
        {
            if (string.IsNullOrEmpty(sha256) || sha256.Length != 64) return false;
            try
            {
                using FileStream fs = File.OpenRead(path);
                using SHA256 sha = SHA256.Create();
                string got = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                return string.Equals(got, sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static void DeleteQuietly(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
        }

        internal static void ResetForTest()
        {
            Stage = RwtInstallStage.Idle;
            Detail = "";
            ActivatedVersion = "";
            LastVerified = false;
            ForceCopyForTest = false;
            Volatile.Write(ref _running, 0);
        }
    }

    internal static class RwtPaths
    {
        internal static string RootOverrideForTest;

        public static string KmhRoot => RootOverrideForTest
            ?? Persistence.KmhDataFolder.Root;
    }
}
