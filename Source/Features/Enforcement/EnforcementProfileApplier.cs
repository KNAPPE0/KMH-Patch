using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Backs up Config once, applies the server's zip profile tracking what it wrote, restores on request; a crash marker drives recovery on next launch.
    internal static class EnforcementProfileApplier
    {
        private static string ConfigPath   => GenFilePaths.ConfigFolderPath;
        private static string RootPath      => Directory.GetParent(GenFilePaths.ConfigFolderPath)?.FullName ?? GenFilePaths.ConfigFolderPath;
        // own folder - a stale ConfigKMH_BACKUP from older builds must never stand in for a full backup
        private static string BackupPath    => Path.Combine(RootPath, "ConfigKMH_Originals");
        private static string ProfilesRoot  => Path.Combine(RootPath, "ConfigKMH_PROFILES");
        private static string StatePath     => Path.Combine(RootPath, "KMH_ENFORCE_STATE.json");
        private static string CrashMark     => Path.Combine(RootPath, "KMH_ENFORCE_APPLYING");
        private static string ActiveMarker  => Path.Combine(ConfigPath, "KMH_ACTIVE_PROFILE.txt");

        // Set while WE are writing, so the watcher ignores our own changes.
        public static volatile bool IsInternalApplyInProgress;

        private static readonly object _applyLock = new object();

        private static readonly object _rxLock = new object();
        private static string _rxHash;
        private static int    _rxChunkCount;
        private static int    _rxTotalBytes;
        private static readonly Dictionary<int, byte[]> _rxChunks = new Dictionary<int, byte[]>();

        private static State _state;
        private static bool  _booted;

        // Call once at mod load: a profile left applied by a crash has to be found before anything reads it.
        public static void Bootstrap()
        {
            if (_booted) return;
            _booted = true;
            try
            {
                Directory.CreateDirectory(ProfilesRoot);
                LoadState();

                // Lock patches install on demand (~0.4s on big modlists); with a profile applied the main menu needs them.
                if (_state != null && _state.IsEnforcedActive)
                    LongEventHandler.ExecuteWhenFinished(Patches.EnforcementSettingsPatches.EnsureInstalled);

                // Died mid-apply: Config could be half the server's and half the player's, so put the personal backup back.
                if (File.Exists(CrashMark) && Directory.Exists(BackupPath))
                {
                    KmhLog.Warn("Enforcement: crash-during-apply marker found - restoring personal backup.");
                    IsInternalApplyInProgress = true;
                    try { RestoreBackupToConfig(softReload: false); }
                    finally { IsInternalApplyInProgress = false; }
                    ClearState();
                    SafeDeleteDir(BackupPath);
                    TryDelete(CrashMark);
                }
            }
            catch (Exception ex) { KmhLog.Warn($"Enforcement bootstrap failed: {ex.Message}"); }
        }

        public static bool   IsApplied   => _state != null && _state.IsEnforcedActive;
        public static string AppliedHash => _state?.ActiveProfileHash ?? "";

        // The mode the active profile was applied under, so the offline lock matches what's actually on disk.
        public static bool PreservePersonalApplied => _state?.PreservePersonal ?? false;

        // The files the active profile wrote (the offline lock list).
        public static IReadOnlyList<string> EnforcedFiles()
            => (_state?.ManagedFiles) ?? (IReadOnlyList<string>)Array.Empty<string>();

        // True if a Mod_<FolderName>_*.xml for this mod is in the applied profile.
        public static bool IsModConfigEnforced(string folderName)
        {
            if (string.IsNullOrEmpty(folderName) || _state?.ManagedFiles == null) return false;
            string prefix = "Mod_" + folderName + "_";
            foreach (string rel in _state.ManagedFiles)
            {
                string name = Path.GetFileName(rel ?? "");
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static void OnProfileBegin(string hash, int chunkCount, int totalBytes)
        {
            lock (_rxLock)
            {
                _rxHash = hash; _rxChunkCount = chunkCount; _rxTotalBytes = totalBytes;
                _rxChunks.Clear();
            }
        }

        public static void OnProfileChunk(string hash, int index, string base64)
        {
            lock (_rxLock)
            {
                // Empty _rxHash means no push is in flight, and two nulls would otherwise compare equal.
                if (string.IsNullOrEmpty(_rxHash) || hash != _rxHash) return;
                try { _rxChunks[index] = Convert.FromBase64String(base64 ?? ""); }
                catch (Exception ex) { KmhLog.Warn($"Enforcement: bad chunk {index}: {ex.Message}"); }
            }
        }

        // A push cut off mid-flight never reaches an End, so without this the partial buffer is held until one arrives.
        public static void ResetReceive() { lock (_rxLock) ResetReceiveLocked(); }

        private static void ResetReceiveLocked()
        {
            _rxChunks.Clear();
            _rxHash = null; _rxChunkCount = 0; _rxTotalBytes = 0;
        }

        public static void OnProfileEnd(string hash)
        {
            byte[] zip;
            lock (_rxLock)
            {
                if (string.IsNullOrEmpty(_rxHash) || hash != _rxHash) return;
                if (_rxChunks.Count != _rxChunkCount)
                {
                    KmhLog.Warn($"Enforcement: profile incomplete ({_rxChunks.Count}/{_rxChunkCount} chunks) - ignoring.");
                    ResetReceiveLocked();
                    return;
                }
                zip = new byte[_rxTotalBytes];
                int off = 0;
                for (int i = 0; i < _rxChunkCount; i++)
                {
                    if (!_rxChunks.TryGetValue(i, out byte[] part)) { KmhLog.Warn("Enforcement: missing chunk on assembly."); return; }
                    Buffer.BlockCopy(part, 0, zip, off, part.Length);
                    off += part.Length;
                }
                _rxChunks.Clear();
            }

            string computed = ConfigProfileUtility.Sha256Hex(zip);
            if (!string.Equals(computed, hash, StringComparison.OrdinalIgnoreCase))
            {
                KmhLog.Warn($"Enforcement: profile hash mismatch (got {computed}, expected {hash}) - ignoring.");
                return;
            }

            // Extract to a per-hash profile folder, then apply on the main thread.
            try
            {
                string profileFolder = GetProfileFolder(hash);
                SafeDeleteDir(profileFolder);
                Directory.CreateDirectory(profileFolder);
                ConfigProfileUtility.ExtractZipBytesToFolder(zip, profileFolder);
            }
            catch (Exception ex) { KmhLog.Error($"Enforcement: extract failed: {ex}"); return; }

            bool alreadyActive = _state != null && _state.IsEnforcedActive
                && string.Equals(_state.ActiveProfileHash, hash, StringComparison.OrdinalIgnoreCase);

            long ticks = DateTime.UtcNow.Ticks;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (alreadyActive) ReapplyIfActive(softReload: true);
                else               ApplyProfile(hash, ticks);
            });
        }

        private static void ApplyProfile(string hash, long updatedTicks)
        {
            lock (_applyLock)
            try
            {
                if (_state == null) _state = new State();
                _state.PreservePersonal = EnforcementCache.PreservePersonal; // capture the mode for this apply
                Directory.CreateDirectory(ConfigPath);
                Directory.CreateDirectory(ProfilesRoot);

                EnsureBackup();                              // one-time full backup of originals
                File.WriteAllText(CrashMark, DateTime.UtcNow.ToString("o"));

                IsInternalApplyInProgress = true;
                int copied;
                try
                {
                    copied = CopyProfileToConfig(GetProfileFolder(hash), updateManifest: true);
                    SoftReload();
                }
                finally
                {
                    IsInternalApplyInProgress = false;
                    TryDelete(CrashMark);
                }

                _state.IsEnforcedActive            = true;
                _state.ActiveProfileHash           = hash;
                _state.ActiveProfileUpdatedUtcTicks = updatedTicks;
                _state.LastAppliedUtcTicks         = DateTime.UtcNow.Ticks;
                _state.LastAppliedFileCount        = copied;
                SaveState();
                WriteActiveMarker(hash, copied);
                StartWatcher();

                KmhLog.Info($"Enforcement: applied profile {hash} - {copied} file(s) into '{ConfigPath}'. " +
                            $"Originals backed up in '{BackupPath}'.");

                // The player already consented (Apply & restart) - restart so launch-only mod configs take effect.
                LongEventHandler.ExecuteWhenFinished(RestartNow);
            }
            catch (Exception ex)
            {
                IsInternalApplyInProgress = false;
                KmhLog.Error($"Enforcement: apply failed: {ex}");
            }
        }

        // Re-writes the active profile over Config with no backup and no restart: drift correction and same-profile reconnect.
        private static void ReapplyIfActive(bool softReload)
        {
            lock (_applyLock)
            try
            {
                if (_state == null || !_state.IsEnforcedActive) return;
                string folder = GetProfileFolder(_state.ActiveProfileHash);
                if (!Directory.Exists(folder)) return;

                IsInternalApplyInProgress = true;
                try
                {
                    int copied = CopyProfileToConfig(folder, updateManifest: false);
                    WriteActiveMarker(_state.ActiveProfileHash, copied);
                    if (softReload) SoftReload();
                }
                finally { IsInternalApplyInProgress = false; }
            }
            catch (Exception ex) { KmhLog.Warn($"Enforcement: reapply failed: {ex.Message}"); }
        }

        // Drops the files an earlier apply managed before copying, and records what this one wrote.
        private static int CopyProfileToConfig(string profileFolder, bool updateManifest)
        {
            Directory.CreateDirectory(ConfigPath);
            RemovePreviouslyManaged();

            var managed = new List<string>();
            int copied = 0;
            bool preserve = _state?.PreservePersonal ?? false;

            foreach (string file in Directory.GetFiles(profileFolder, "*", SearchOption.AllDirectories))
            {
                string rel = MakeRelative(profileFolder, file);
                if (string.IsNullOrEmpty(rel)) continue;
                if (ConfigProfileUtility.ShouldExcludeByRelPath(rel)) continue;             // personal/cache/dir backstop
                string nameOnly = Path.GetFileName(file);
                if (EnforcementMods.IsSafeModConfigFile(nameOnly)) continue;                // safe mods stay editable

                string dest = Path.Combine(ConfigPath, rel);
                try
                {
                    string destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                    // Preserve-personal: merge existing mod-settings xml; else copy.
                    if (preserve && File.Exists(dest)
                        && nameOnly.StartsWith("Mod_", StringComparison.OrdinalIgnoreCase)
                        && nameOnly.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        File.WriteAllText(dest, ConfigMerge.MergePreservingPersonal(File.ReadAllText(file), File.ReadAllText(dest)));
                    else
                        File.Copy(file, dest, overwrite: true);

                    managed.Add(rel);
                    copied++;
                }
                catch (Exception ex) { KmhLog.Warn($"Enforcement: couldn't apply '{rel}': {ex.Message}"); }
            }

            if (updateManifest)
            {
                if (_state == null) _state = new State();
                _state.ManagedFiles = managed;
            }
            return copied;
        }

        private static void RemovePreviouslyManaged()
        {
            if (_state?.ManagedFiles == null) return;
            foreach (string rel in _state.ManagedFiles)
            {
                // ManagedFiles comes back from disk, not a live enumeration, so re-check containment before an irreversible delete.
                string full = ResolveUnderConfig(rel);
                if (full == null) { KmhLog.Warn($"Enforcement: managed entry '{rel}' resolves outside Config - not deleting."); continue; }
                TryDelete(full);
            }
        }

        // Null if the entry escapes Config; the trailing separator stops a sibling folder ("Config_old") passing the prefix test.
        private static string ResolveUnderConfig(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return null;
            try
            {
                string root = Path.GetFullPath(ConfigPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(Path.Combine(ConfigPath, rel));
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
            }
            catch { return null; }
        }

        public static void Restore() => RestoreInternal(softReload: true);

        private static void RestoreInternal(bool softReload)
        {
            lock (_applyLock)
            try
            {
                StopWatcher();
                if (!Directory.Exists(BackupPath))
                {
                    // Nothing was ever applied - just clear state.
                    ClearState();
                    KmhLog.Info("Enforcement: restore requested but no backup exists - cleared state.");
                    return;
                }

                IsInternalApplyInProgress = true;
                try { RestoreBackupToConfig(softReload); }
                finally { IsInternalApplyInProgress = false; }

                ClearState();
                SafeDeleteDir(BackupPath);
                KmhLog.Info($"Enforcement: restored personal configs from '{BackupPath}' and cleared enforcement.");
            }
            catch (Exception ex)
            {
                IsInternalApplyInProgress = false;
                KmhLog.Error($"Enforcement: restore failed: {ex}");
            }
        }

        // Fail-LOUD: an incomplete backup must throw so ApplyProfile aborts rather than overwriting originals with no way back.
        private static void EnsureBackup()
        {
            if (Directory.Exists(BackupPath)) return;   // already have the (complete) originals
            if (!Directory.Exists(ConfigPath)) { Directory.CreateDirectory(BackupPath); return; } // no configs to save - empty backup restores cleanly
            string tmp = BackupPath + ".partial";
            SafeDeleteDir(tmp);
            Directory.CreateDirectory(tmp);
            CopyDir(ConfigPath, tmp);           // a failure here throws OUT (not swallowed) -> ApplyProfile aborts, originals untouched
            Directory.Move(tmp, BackupPath);    // atomic: BackupPath appears only once the copy is complete
        }

        private static void RestoreBackupToConfig(bool softReload)
        {
            Directory.CreateDirectory(ConfigPath);
            WipeContents(ConfigPath);
            CopyDir(BackupPath, ConfigPath);
            if (softReload) SoftReload();
        }

        private static FileSystemWatcher _watcher;
        private static CancellationTokenSource _watchToken;
        private static int _pendingReapply;
        private static DateTime _lastReapplyUtc = DateTime.MinValue;

        private static void StartWatcher()
        {
            try
            {
                StopWatcher();
                if (_state == null || !_state.IsEnforcedActive || !Directory.Exists(ConfigPath)) return;

                _watcher = new FileSystemWatcher(ConfigPath)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents   = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.Size,
                };
                _watcher.Changed += OnConfigChanged;
                _watcher.Created += OnConfigChanged;
                _watcher.Deleted += OnConfigChanged;
                _watcher.Renamed += OnConfigChanged;

                _watchToken = new CancellationTokenSource();
                _ = WatcherLoopAsync(_watchToken.Token);
            }
            catch { StopWatcher(); }
        }

        public static void StopWatching() => StopWatcher();

        private static void StopWatcher()
        {
            try
            {
                _watchToken?.Cancel();
                _watchToken = null;
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Changed -= OnConfigChanged;
                    _watcher.Created -= OnConfigChanged;
                    _watcher.Deleted -= OnConfigChanged;
                    _watcher.Renamed -= OnConfigChanged;
                    _watcher.Dispose();
                    _watcher = null;
                }
                _pendingReapply = 0;
            }
            catch { }
        }

        private static void OnConfigChanged(object sender, FileSystemEventArgs e)
        {
            if (IsInternalApplyInProgress) return;
            if (_state == null || !_state.IsEnforcedActive) return;
            // Exempt admins edit freely (respects AdminBypass).
            if (EnforcementCache.Enabled && EnforcementCache.AdminBypass && EnforcementCache.IsAdmin) return;
            Interlocked.Exchange(ref _pendingReapply, 1);
        }

        private static async Task WatcherLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(250, token); }
                catch (TaskCanceledException) { return; }

                if (_state == null || !_state.IsEnforcedActive) continue;
                if (Interlocked.CompareExchange(ref _pendingReapply, 0, 1) != 1) continue;
                if ((DateTime.UtcNow - _lastReapplyUtc).TotalMilliseconds < 1000) continue;
                _lastReapplyUtc = DateTime.UtcNow;

                try { ReapplyIfActive(softReload: false); KmhLog.Info("Enforcement: reverted local config edit(s)."); }
                catch { }
            }
        }

        private static void RestartNow()
        {
            try
            {
                Messages.Message("Applying this server's enforced mod configs - restarting RimWorld...",
                    MessageTypeDefOf.NeutralEvent, historical: false);
                GenCommandLine.Restart();
            }
            catch (Exception ex) { KmhLog.Warn($"Enforcement: restart failed: {ex.Message}"); }
        }

        private static string GetProfileFolder(string hash) => Path.Combine(ProfilesRoot, SafeHash(hash));

        private static string SafeHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return "none";
            var sb = new System.Text.StringBuilder(hash.Length);
            foreach (char c in hash)
                sb.Append((char.IsLetterOrDigit(c)) ? c : '_');
            return sb.ToString();
        }

        private static void SoftReload()
        {
            try
            {
                MethodInfo m = typeof(LoadedModManager).GetMethod("ReadModSettings",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                m?.Invoke(null, null);
            }
            catch (Exception ex) { KmhLog.Warn($"Enforcement: soft reload failed: {ex.Message}"); }
        }

        private static void WriteActiveMarker(string hash, int copied)
        {
            try
            {
                File.WriteAllText(ActiveMarker,
                    $"KMH config profile active{Environment.NewLine}" +
                    $"Hash={hash}{Environment.NewLine}" +
                    $"AppliedUtc={DateTime.UtcNow:o}{Environment.NewLine}" +
                    $"Files={copied}{Environment.NewLine}");
            }
            catch { }
        }

        private static void CopyDir(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            if (!Directory.Exists(sourceDir)) return;

            foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string rel = dir.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(targetDir, rel));
            }
            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string rel  = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
                string dest = Path.Combine(targetDir, rel);
                string df   = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(df)) Directory.CreateDirectory(df);
                try { File.Copy(file, dest, overwrite: true); } catch { }
            }
        }

        private static void WipeContents(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
                TryDelete(file);
            foreach (string sub in Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                SafeDeleteDir(sub);
        }

        private static string MakeRelative(string root, string fullPath)
        {
            string r = Path.GetFullPath(root);
            if (!r.EndsWith(Path.DirectorySeparatorChar.ToString())) r += Path.DirectorySeparatorChar;
            string f = Path.GetFullPath(fullPath);
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? f.Substring(r.Length) : "";
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        private static void SafeDeleteDir(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

        private static void LoadState()
        {
            try { if (File.Exists(StatePath)) _state = JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath)); }
            catch { }
            if (_state == null) _state = new State();
        }

        private static void SaveState()
        {
            try { File.WriteAllText(StatePath, JsonConvert.SerializeObject(_state, Formatting.Indented)); }
            catch (Exception ex) { KmhLog.Warn($"Enforcement: save state failed: {ex.Message}"); }
        }

        private static void ClearState()
        {
            _state = new State();
            SaveState();
            TryDelete(ActiveMarker);
        }

        private class State
        {
            public bool         IsEnforcedActive            { get; set; }
            public string       ActiveProfileHash           { get; set; } = "";
            public long         ActiveProfileUpdatedUtcTicks { get; set; }
            public long         LastAppliedUtcTicks         { get; set; }
            public int          LastAppliedFileCount        { get; set; }
            public bool         PreservePersonal            { get; set; }
            public List<string> ManagedFiles                { get; set; } = new List<string>();
            // Boot-loop guard for forced restart.
            public string       LastRestartHash             { get; set; } = "";
            public string       LastRestartUtc              { get; set; } = "";
        }
    }
}
