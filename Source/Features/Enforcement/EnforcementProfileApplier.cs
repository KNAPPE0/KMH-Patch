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
    // The KMH_ENFORCE_APPLYING marker lets the next launch recover from a crash mid-apply.
    internal static class EnforcementProfileApplier
    {
        internal static string RootOverrideForTest;
        internal static Action<Action> QueueForTest;
        internal static Action RestartForTest;
        internal static Action<string> StateReadbackForTest;
        internal static Func<IList<Mods.InstalledMod>> InstalledModsForTest;
        private static string ConfigPath   => RootOverrideForTest == null ? GenFilePaths.ConfigFolderPath : Path.Combine(RootOverrideForTest, "Config");
        private static string RootPath      => RootOverrideForTest ?? (Directory.GetParent(GenFilePaths.ConfigFolderPath)?.FullName ?? GenFilePaths.ConfigFolderPath);
        // Own folder: a stale ConfigKMH_BACKUP from older builds must never stand in for a full backup.
        private static string BackupPath    => Path.Combine(RootPath, "ConfigKMH_Originals");
        private static string ProfilesRoot  => Path.Combine(RootPath, "ConfigKMH_PROFILES");
        private static string StatePath     => Path.Combine(RootPath, "KMH_ENFORCE_STATE.json");
        private static string CrashMark     => Path.Combine(RootPath, "KMH_ENFORCE_APPLYING");
        // The complete candidate Config is built here first; nothing touches the live folder until it validates.
        private static string StagingPath   => Path.Combine(RootPath, "ConfigKMH_STAGING");
        private static string ActiveMarker  => Path.Combine(ConfigPath, "KMH_ACTIVE_PROFILE.txt");
        private static string RollbackPath => Path.Combine(RootPath, "ConfigKMH_ROLLBACK");
        private static readonly string BootId = Guid.NewGuid().ToString("N");
        internal static bool RecoveryRequired { get; private set; }
        internal static string AppliedServerId => _state?.ServerId ?? "";
        // Older builds could leave the saved copy of the player's mod list holding a server's list; found at boot.
        internal static bool DamagedModListFound { get; private set; }

        // Put back while this process runs: mods already hold the replaced values in memory, and a save now would
        // write those over the player's own. Held, write-blocked, until RimWorld restarts.
        private static readonly HashSet<string> _awaitingRestart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static bool RestoreAwaitsRestart { get { lock (_awaitingRestart) return _awaitingRestart.Count > 0; } }

        internal static bool AwaitsRestart(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            lock (_awaitingRestart) return _awaitingRestart.Contains(Path.GetFileName(fileName));
        }

        internal static bool ModAwaitsRestart(string folderName)
        {
            if (string.IsNullOrEmpty(folderName)) return false;
            string prefix = "Mod_" + folderName + "_";
            lock (_awaitingRestart)
                foreach (string name in _awaitingRestart)
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void MarkAwaitingRestart(IEnumerable<string> names)
        {
            if (names == null) return;
            int added = 0;
            lock (_awaitingRestart)
                foreach (string n in names)
                    if (!string.IsNullOrEmpty(n) && _awaitingRestart.Add(Path.GetFileName(n))) added++;
            if (added == 0) return;
            Patches.ModSettingsWriteGuard.EnsureInstalled();
            Action install = Patches.EnforcementSettingsPatches.EnsureInstalled;
            if (QueueForTest != null) QueueForTest(install); else LongEventHandler.ExecuteWhenFinished(install);
        }

        internal static void ClearAwaitingRestartForTest() { lock (_awaitingRestart) _awaitingRestart.Clear(); }

        // Set while enforcement itself writes, so the watcher ignores those changes.
        public static volatile bool IsInternalApplyInProgress;

        private static readonly object _applyLock = new object();

        private static readonly object _rxLock = new object();
        private static string _rxHash;
        private static int    _rxChunkCount;
        private static int    _rxTotalBytes;
        private static long   _rxHeldBytes;
        private static readonly Dictionary<int, byte[]> _rxChunks = new Dictionary<int, byte[]>();

        // Mirrors the server's send limits: the declared size is allocated before the hash can prove anything.
        internal const int MaxRxChunkBytes = 32 * 1024;
        internal const int MaxRxChunks     = 4096;
        internal const int MaxRxTotalBytes = MaxRxChunks * MaxRxChunkBytes;

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

                // Lock patches install on demand (~0.4s on big modlists); an applied profile needs them at the menu.
                if (_state != null && _state.IsEnforcedActive)
                    LongEventHandler.ExecuteWhenFinished(Patches.EnforcementSettingsPatches.EnsureInstalled);

                // A candidate from a run that never committed is not evidence of anything; the marker is.
                SafeDeleteDir(StagingPath);

                // Died mid-apply: Config could be half the server's and half the player's, so replay the journal.
                if (File.Exists(CrashMark) && Directory.Exists(RollbackPath))
                {
                    KmhLog.Warn("Enforcement: recovering interrupted config transaction.");
                    string prior = File.ReadAllText(CrashMark);
                    var restored = JsonConvert.DeserializeObject<State>(prior);
                    if (restored == null) throw new InvalidDataException("Invalid rollback state");
                    // Mods constructed before this one have already read the half-applied files.
                    MarkAwaitingRestart(BaselinePaths(RollbackPath));
                    if (!ConfigBaseline.Restore(RollbackPath, ConfigPath, out string rwhy))
                        throw new IOException("The interrupted config transaction could not be rolled back: " + rwhy);
                    _state = restored;
                    SaveState();
                    File.Delete(CrashMark);
                }
                else if (File.Exists(CrashMark) && Directory.Exists(BackupPath))
                {
                    KmhLog.Warn("Enforcement: crash-during-apply marker found - restoring personal backup.");
                    IsInternalApplyInProgress = true;
                    try { RestoreBackupToConfig(softReload: false); }
                    finally { IsInternalApplyInProgress = false; }
                    ClearState();
                    SafeDeleteDir(BackupPath);
                    TryDelete(CrashMark);
                }
                else if (File.Exists(CrashMark))
                    throw new InvalidDataException("Interrupted config transaction has no recovery backup.");

                // After the Config recovery above, so the mod list follows whichever state it left in place.
                ModsConfigJournal.Recover(RootPath, _state?.ApplyGeneration ?? "");
                DamagedModListFound = ModsConfigJournal.QuarantineDamagedOriginal(RootPath);
                CheckBoot();
                RefuseUnsafeApplied();
                PruneProfiles();
            }
            catch (Exception ex) { RecoveryRequired = true; KmhLog.Warn($"Enforcement bootstrap failed: {ex.Message}"); }
        }

        private static IEnumerable<string> BaselinePaths(string backupDir)
        {
            ConfigBaseline b = ConfigBaseline.Verify(backupDir, out _);
            var names = new List<string>();
            if (b != null) foreach (BaselineEntry e in b.Entries) names.Add(e.Path);
            return names;
        }

        // Files still byte-identical to the server's copy after an older build lost the backup: never declared the
        // player's own silently, but listed so the player can keep them or let those mods start from their defaults.
        private static string LeftoversPath => Path.Combine(RootPath, "KMH_ENFORCE_LEFTOVERS.json");

        private sealed class Leftovers
        {
            public string ServerId = "";
            public string ProfileHash = "";
            public List<string> Files = new List<string>();
        }

        internal static IReadOnlyList<string> ServerLeftovers()
        {
            try
            {
                if (!File.Exists(LeftoversPath)) return Array.Empty<string>();
                Leftovers l = JsonConvert.DeserializeObject<Leftovers>(File.ReadAllText(LeftoversPath));
                var safe = new List<string>();
                if (l?.Files != null) foreach (string f in l.Files) if (Enforceable(f)) safe.Add(f);
                return safe;
            }
            catch (Exception ex) { KmhLog.Warn($"Enforcement: the list of server settings left in place is unreadable: {ex.Message}"); return Array.Empty<string>(); }
        }

        internal static void KeepServerLeftovers()
        {
            try { if (File.Exists(LeftoversPath)) File.Delete(LeftoversPath); }
            catch (Exception ex) { KmhLog.Warn($"Enforcement: could not clear the leftover list: {ex.Message}"); }
        }

        // Only a file still exactly the server's copy is removed; one the player has edited since is theirs.
        internal static bool ResetServerLeftovers(out int removed, out string why)
        {
            removed = 0; why = "";
            try
            {
                Leftovers l = File.Exists(LeftoversPath) ? JsonConvert.DeserializeObject<Leftovers>(File.ReadAllText(LeftoversPath)) : null;
                if (l?.Files == null) return true;
                string profile = GetProfileFolder(l.ProfileHash);
                var gone = new List<string>();
                foreach (string rel in l.Files)
                {
                    if (!Enforceable(rel)) continue;
                    string live = ResolveUnder(ConfigPath, rel), copy = ResolveUnder(profile, rel);
                    if (live == null || copy == null || !File.Exists(live) || !File.Exists(copy)) continue;
                    if (HashFile(live) != HashFile(copy)) continue;
                    gone.Add(rel);
                }
                MarkAwaitingRestart(gone);
                IsInternalApplyInProgress = true;
                try { foreach (string rel in gone) { File.Delete(ResolveUnder(ConfigPath, rel)); removed++; } }
                finally { IsInternalApplyInProgress = false; }
                File.Delete(LeftoversPath);
                KmhLog.Info($"Enforcement: {removed} mod setting file(s) a server left in place were removed; those mods start from their defaults.");
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        private static void RecordLeftovers()
        {
            if (_state == null) return;
            string profile = GetProfileFolder(_state.ActiveProfileHash);
            if (!Directory.Exists(profile)) return;
            var still = new List<string>();
            foreach (string rel in _state.ManagedFiles ?? new List<string>())
            {
                if (!Enforceable(rel)) continue;
                string live = ResolveUnder(ConfigPath, rel), copy = ResolveUnder(profile, rel);
                if (live != null && copy != null && File.Exists(live) && File.Exists(copy) && HashFile(live) == HashFile(copy))
                    still.Add(rel);
            }
            if (still.Count == 0) return;
            Persistence.KmhAtomicFile.Write(LeftoversPath, JsonConvert.SerializeObject(
                new Leftovers { ServerId = _state.ServerId ?? "", ProfileHash = _state.ActiveProfileHash ?? "", Files = still }, Formatting.Indented));
            KmhLog.Warn($"Enforcement: {still.Count} mod setting file(s) still hold a server's settings and no copy of yours was ever kept; "
                      + "they are listed on the main menu to keep or reset.");
        }

        internal enum BaselineState { None, Verified, Damaged }

        // Never promise a restore that cannot run: the player would keep server settings thinking one click undoes them.
        internal static BaselineState BaselineHealth(out string detail, out int files)
        {
            files = 0;
            ConfigBaseline b = ConfigBaseline.Verify(BackupPath, out string why);
            if (b != null) { files = b.Count; detail = ""; return BaselineState.Verified; }
            if (!Directory.Exists(BackupPath)) { detail = "no personal configs have been recorded yet"; return BaselineState.None; }
            detail = why;
            return BaselineState.Damaged;
        }

        public static bool   IsApplied   => _state != null && _state.IsEnforcedActive;

        // The boot an apply's own restart lands in is not "after leaving": the player has just accepted that server.
        internal static bool AppliedThisSession
            => _state != null && _state.LastAppliedUtcTicks > 0
            && DateTime.UtcNow - new DateTime(_state.LastAppliedUtcTicks, DateTimeKind.Utc) < TimeSpan.FromMinutes(20);
        internal static bool RestoresModList => ModsConfigJournal.HasOriginal(RootPath);
        // Shared with the browser's join preparation, so the player's own list survives any number of servers.
        internal static string ModsJournalRoot => RootPath;
        public static string AppliedHash => _state?.ActiveProfileHash ?? "";
        public static string AppliedOrderHash => _state?.ActiveOrderHash ?? "";

        // The mode the active profile was applied under, so the offline lock matches what's actually on disk.
        public static bool PreservePersonalApplied => _state?.PreservePersonal ?? false;

        public static IReadOnlyList<string> EnforcedFiles()
            => (_state?.ManagedFiles) ?? (IReadOnlyList<string>)Array.Empty<string>();

        internal static bool IsFileEnforced(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !IsApplied || _state.ManagedFiles == null) return false;
            foreach (string rel in _state.ManagedFiles)
                if (string.Equals(Path.GetFileName(rel ?? ""), fileName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

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
                _rxChunks.Clear();
                _rxHeldBytes = 0;
                if (string.IsNullOrEmpty(hash) || chunkCount <= 0 || chunkCount > MaxRxChunks
                    || totalBytes <= 0 || totalBytes > MaxRxTotalBytes)
                {
                    KmhLog.Warn($"Enforcement: refusing a config profile declaring {chunkCount} chunk(s) / "
                              + $"{totalBytes} byte(s) - the limit is {MaxRxChunks} / {MaxRxTotalBytes}.");
                    _rxHash = null; _rxChunkCount = 0; _rxTotalBytes = 0;
                    return;
                }
                // Nothing is buffered for a push the player did not ask for: a consented apply or the applied profile's re-assert.
                if (!EnforcementFlow.MayApply(hash))
                {
                    KmhLog.Warn("Enforcement: ignoring a config profile the player did not ask for.");
                    _rxHash = null; _rxChunkCount = 0; _rxTotalBytes = 0;
                    return;
                }
                _rxHash = hash; _rxChunkCount = chunkCount; _rxTotalBytes = totalBytes;
            }
        }

        public static void OnProfileChunk(string hash, int index, string base64)
        {
            lock (_rxLock)
            {
                // Empty _rxHash means the push was refused outright, and two nulls would otherwise compare equal.
                if (string.IsNullOrEmpty(_rxHash) || hash != _rxHash) return; // stale chunk from a superseded push
                if (index < 0 || index >= _rxChunkCount)
                {
                    KmhLog.Warn($"Enforcement: chunk index {index} is outside 0..{_rxChunkCount - 1} - ignoring.");
                    return;
                }
                byte[] part;
                // Measured before decoding, so an oversized chunk costs nothing to refuse.
                if ((base64?.Length ?? 0) > (MaxRxChunkBytes + 2) / 3 * 4)
                {
                    KmhLog.Warn($"Enforcement: chunk {index} is longer than any {MaxRxChunkBytes}-byte chunk encodes to - ignoring.");
                    return;
                }
                try { part = Convert.FromBase64String(base64 ?? ""); }
                catch (Exception ex) { KmhLog.Warn($"Enforcement: bad chunk {index}: {ex.Message}"); return; }
                if (part.Length > MaxRxChunkBytes)
                {
                    KmhLog.Warn($"Enforcement: chunk {index} is {part.Length} bytes, over the {MaxRxChunkBytes} limit - ignoring.");
                    return;
                }
                // Counted as a replacement, so re-sending one chunk cannot walk the total past what was declared.
                long previous = _rxChunks.TryGetValue(index, out byte[] old) ? old.Length : 0;
                long held = _rxHeldBytes - previous + part.Length;
                if (held > _rxTotalBytes)
                {
                    KmhLog.Warn($"Enforcement: chunks exceed the declared {_rxTotalBytes} bytes - ignoring chunk {index}.");
                    return;
                }
                _rxChunks[index] = part;
                _rxHeldBytes = held;
            }
        }

        // A push cut off mid-flight never reaches End; without this the partial buffer is held indefinitely.
        public static void ResetReceive() { lock (_rxLock) ResetReceiveLocked(); }

        private static void ResetReceiveLocked()
        {
            _rxChunks.Clear();
            _rxHeldBytes = 0; _rxHash = null; _rxChunkCount = 0; _rxTotalBytes = 0;
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
                // Checked before allocating: short chunks leave a zero-padded tail, long ones overrun the copy.
                if (_rxHeldBytes != _rxTotalBytes)
                {
                    KmhLog.Warn($"Enforcement: profile declared {_rxTotalBytes} bytes but {_rxHeldBytes} arrived - ignoring.");
                    _rxChunks.Clear(); _rxHeldBytes = 0; _rxHash = null;
                    return;
                }
                zip = new byte[_rxTotalBytes];
                int off = 0;
                for (int i = 0; i < _rxChunkCount; i++)
                {
                    if (!_rxChunks.TryGetValue(i, out byte[] part)) { KmhLog.Warn("Enforcement: missing chunk on assembly."); return; }
                    if (off + part.Length > zip.Length) { KmhLog.Warn("Enforcement: chunk assembly overran the declared size - ignoring."); return; }
                    Buffer.BlockCopy(part, 0, zip, off, part.Length);
                    off += part.Length;
                }
                ResetReceiveLocked();
            }

            string computed = ConfigProfileUtility.Sha256Hex(zip);
            if (!string.Equals(computed, hash, StringComparison.OrdinalIgnoreCase))
            {
                KmhLog.Warn($"Enforcement: profile hash mismatch (got {computed}, expected {hash}) - ignoring.");
                return;
            }

            int connectionGeneration = SubProtocol.KmhDispatcher.SessionGeneration;
            string serverId = SubProtocol.KmhDispatcher.ServerId;
            long ticks = DateTime.UtcNow.Ticks;
            Action<Action> queue = QueueForTest ?? LongEventHandler.ExecuteWhenFinished;
            queue(() =>
            {
                lock (_applyLock)
                {
                    if (connectionGeneration != SubProtocol.KmhDispatcher.SessionGeneration
                        || serverId != SubProtocol.KmhDispatcher.ServerId) return;
                    if (RecoveryRequired) { EnforcementAdmission.Fail("Config recovery is required."); return; }
                    if (!EnforcementFlow.MayApply(hash)) { EnforcementAdmission.Fail("Profile arrived without consent."); return; }
                    bool consentedNow = EnforcementFlow.ConsentedNow(hash);
                    // Config is untouched yet: refuse this server only; marking recovery would lock out every server.
                    if (!TryUnpackProfile(zip, GetProfileFolder(hash), out string unpackWhy))
                    { EnforcementAdmission.Fail("Config profile refused: " + unpackWhy); return; }
                    string unsafeWhy = ConfigXmlGuard.FolderRefusal(GetProfileFolder(hash), Enforceable);
                    if (unsafeWhy != null)
                    {
                        KmhLog.Warn($"Enforcement: refused config profile {hash}: {unsafeWhy}");
                        EnforcementAdmission.Fail("Config profile refused: " + unsafeWhy);
                        return;
                    }
                    try
                    {
                        bool same = _state != null && _state.IsEnforcedActive && _state.ServerId == serverId
                            && _state.PreservePersonal == EnforcementCache.PreservePersonal
                            && _state.SafePolicy == SafePolicy()
                            && string.Equals(_state.ActiveProfileHash, hash, StringComparison.OrdinalIgnoreCase);
                        // Settings and mod order must both verify before release; either failing means a fresh apply.
                        string orderWhy = "";
                        if (same && ManagedFilesMatch() && OrderVerified(out orderWhy))
                        {
                            ProveDurableState();
                            if (_state.RestartBootId != BootId)
                            { StartWatcher(); EnforcementFlow.Clear(); EnforcementAdmission.Complete(); }
                            return;
                        }
                        if (same && !string.IsNullOrEmpty(orderWhy))
                            KmhLog.Info($"ModSync: re-applying because {orderWhy}.");
                        // New settings or a new mod list are shown before anything is committed; a re-assert of the applied ones is not.
                        List<string> order = EnforcementCache.ModOrder;
                        string orderFingerprint = EnforcementCache.OrderFingerprint(order);
                        bool newList = !string.Equals(_state?.ActiveOrderHash ?? "", orderFingerprint, StringComparison.OrdinalIgnoreCase);
                        if ((!same || newList) && !EnforcementFlow.Reviewed(hash))
                        {
                            EnforcementFlow.ShowReview(hash, DescribeProfile(hash), order.Count > 0 ? PlanOrder(order) : null, orderFingerprint, () => queue(() =>
                            {
                                lock (_applyLock)
                                {
                                    if (connectionGeneration != SubProtocol.KmhDispatcher.SessionGeneration
                                        || serverId != SubProtocol.KmhDispatcher.ServerId || RecoveryRequired || !EnforcementFlow.MayApply(hash)
                                        || !EnforcementFlow.Reviewed(hash)) return;
                                    try { ApplyProfile(hash, ticks, consentedNow: true); }
                                    catch (Exception ex) { RecoveryRequired = true; EnforcementAdmission.Fail("Config apply failed: " + ex.Message); }
                                }
                            }));
                            return;
                        }
                        ApplyProfile(hash, ticks, consentedNow);
                    }
                    catch (Exception ex) { RecoveryRequired = true; EnforcementAdmission.Fail("Config verification failed: " + ex.Message); }
                }
            });
        }

        internal enum ReviewKind { Adds, Replaces, Merges, Unused }

        internal sealed class ReviewItem
        {
            public string File = "";
            public string Mod = "";
            public ReviewKind Kind;
        }

        // What the apply would do to each file, worked out from the unpacked profile and the live Config, before any write.
        internal static List<ReviewItem> DescribeProfile(string hash)
        {
            var items = new List<ReviewItem>();
            string folder = GetProfileFolder(hash);
            if (!Directory.Exists(folder)) return items;
            bool preserve = EnforcementCache.PreservePersonal;
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (ConfigProfileUtility.ShouldExcludeByRelPath(name) || EnforcementMods.IsSafeModConfigFile(name) || !Enforceable(name)) continue;
                string live = ResolveUnder(ConfigPath, name);
                if (live == null) continue;
                ModContentPack mod = ModForSettingsFile(name);
                bool exists = File.Exists(live);
                if (mod != null && exists && HashFile(live) == HashFile(file)) continue;
                ReviewKind kind = mod == null ? ReviewKind.Unused
                    : !exists ? ReviewKind.Adds
                    : preserve && name.StartsWith("Mod_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                        ? ReviewKind.Merges : ReviewKind.Replaces;
                items.Add(new ReviewItem { File = name, Mod = mod?.Name ?? "", Kind = kind });
            }
            return items;
        }

        internal static Func<string, ModContentPack> ModForSettingsFileForTest;

        // RimWorld names a mod's settings Mod_<folder>_<class>.xml; the longest matching folder wins.
        private static ModContentPack ModForSettingsFile(string name)
        {
            if (ModForSettingsFileForTest != null) return ModForSettingsFileForTest(name);
            ModContentPack best = null;
            foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
            {
                if (string.IsNullOrEmpty(m?.FolderName)) continue;
                if (name.StartsWith("Mod_" + m.FolderName + "_", StringComparison.OrdinalIgnoreCase)
                    && (best == null || m.FolderName.Length > best.FolderName.Length)) best = m;
            }
            return best;
        }

        // Unpacked beside the folder and swapped in whole, so a failure leaves the previous copy as it was.
        private static bool TryUnpackProfile(byte[] zip, string folder, out string why)
        {
            why = "";
            string incoming = folder + ".unpack-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                ConfigProfileUtility.ExtractZipBytesToFolder(zip, incoming);
                SafeDeleteDir(folder);
                Directory.Move(incoming, folder);
                return true;
            }
            catch (Exception ex)
            {
                why = ex.Message;
                try { SafeDeleteDir(incoming); } catch { }
                return false;
            }
        }

        // Thrown only before the first live write, so it says nothing in Config needs recovering.
        internal static Action PreflightForTest;

        // consentedNow: the player agreed to this profile in this session, which alone may restart RimWorld unasked.
        private static void ApplyProfile(string hash, long updatedTicks, bool consentedNow = false)
        {
            // Before the commit only staging and the baseline are written, so a failure there just refuses the profile.
            bool live = false;
            string previousState = null;
            lock (_applyLock)
            try
            {
                // Read once: the list checked against consent is the list written, whatever a snapshot does meanwhile.
                List<string> published = EnforcementCache.ModOrder;
                string orderFingerprint = EnforcementCache.OrderFingerprint(published);
                // A repair runs without asking, so it may only write a list the player has already agreed to.
                if (!EnforcementFlow.OrderAgreed(orderFingerprint))
                {
                    KmhLog.Warn("Enforcement: the server's mod list changed after you agreed to it; nothing was applied until you agree to the new one.");
                    return;
                }

                // Refused before anything is touched, by the rules the browser's mod plan applies.
                List<string> plannedOrder = null;
                if (published.Count > 0)
                {
                    Mods.KmhModPlan plan = PlanOrder(published);
                    if (!plan.Ok) { EnforcementAdmission.Fail("Mod order refused: " + plan.Blocked); return; }
                    plannedOrder = plan.Order;
                }

                if (_state == null) _state = new State();
                previousState = JsonConvert.SerializeObject(_state);
                _state.PreservePersonal = EnforcementCache.PreservePersonal;
                Directory.CreateDirectory(ConfigPath);
                Directory.CreateDirectory(ProfilesRoot);

                IsInternalApplyInProgress = true;
                int copied;
                bool changed;
                List<string> removed;
                try
                {
                    copied = BuildCandidate(GetProfileFolder(hash), StagingPath, updateManifest: true, out removed);

                    // Originals are recorded only once the apply is viable, and only for the files it will touch.
                    EnsureBaselineCovers(Touched(removed));

                    changed = !CandidateMatchesLive(StagingPath, removed) || _state.RestartBootId == BootId;
                    PreflightForTest?.Invoke();
                    live = true;
                    if (changed) CommitCandidate(StagingPath, removed, previousState);
                }
                catch
                {
                    SafeDeleteDir(StagingPath);
                    throw;
                }
                finally { IsInternalApplyInProgress = false; }

                // Mod order applies in this same transaction: one consent, one durable state, one restart.
                string generation = Guid.NewGuid().ToString("N");
                ModSyncApply.Result order = ApplyOrderInTransaction(plannedOrder, generation, out bool orderChanged);
                if (order != null && !order.Applied)
                {
                    SafeDeleteDir(StagingPath);
                    bool exact = ModsConfigJournal.RollBack(RootPath);
                    RecoveryRequired = !order.RolledBack || !exact;
                    EnforcementAdmission.Fail("Mod order could not be applied: " + order.Failure);
                    return;
                }
                changed = changed || orderChanged;

                _state.IsEnforcedActive            = true;
                _state.ActiveProfileHash           = hash;
                _state.ActiveOrderHash             = orderFingerprint;
                _state.ActiveProfileUpdatedUtcTicks = updatedTicks;
                _state.LastAppliedUtcTicks         = DateTime.UtcNow.Ticks;
                _state.LastAppliedFileCount        = copied;
                _state.ServerId = SubProtocol.KmhDispatcher.ServerId;
                _state.SafePolicy = SafePolicy();
                _state.ApplyGeneration = generation;
                _state.RestartBootId = changed ? BootId : "";
                _state.RestartConsumed = false;
                _state.ManagedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rel in _state.ManagedFiles)
                    _state.ManagedHashes.Add(rel, HashFile(ResolveUnder(ConfigPath, rel)));
                SaveState();
                File.Delete(CrashMark);
                if (orderChanged) ModsConfigJournal.Commit(RootPath, plannedOrder);
                WriteActiveMarker(hash, copied);
                StartWatcher();

                KmhLog.Info($"Enforcement: applied profile {hash} - {copied} file(s) into '{ConfigPath}'. " +
                            $"Originals backed up in '{BackupPath}'.");

                PruneProfiles();
                // The restart makes launch-only mod settings take effect; the boot check catches one that never starts.
                if (changed) { MarkBootCheck(hash); RestartNow(automatic: consentedNow); }
                else if (OrderVerified(out string releaseWhy)) { EnforcementFlow.Clear(); EnforcementAdmission.Complete(); }
                else EnforcementAdmission.Fail("Mod order could not be verified: " + releaseWhy);
            }
            catch (Exception ex)
            {
                IsInternalApplyInProgress = false;
                if (!live)
                {
                    // The in-memory state was edited for the candidate; put it back as the disk still has it.
                    try { SafeDeleteDir(StagingPath); } catch { }
                    if (previousState != null) _state = JsonConvert.DeserializeObject<State>(previousState);
                    KmhLog.Warn($"Enforcement: profile {hash} refused before anything was applied: {ex.Message}");
                    EnforcementAdmission.Fail("Config profile refused: " + ex.Message);
                    return;
                }
                RecoveryRequired = true;
                // The Config journal never reached ModsConfig.xml, so the order this apply wrote is undone here.
                ModsConfigJournal.RollBack(RootPath);
                RollBackFailedApply();
                KmhLog.Error($"Enforcement: apply failed: {ex}");
                EnforcementAdmission.Fail("Config apply or durable state failed. Recovery is required.");
            }
        }

        // Watcher work is serialized on the main thread; launch-only settings need a real repair transaction.
        private static void ReapplyIfActive(bool softReload)
        {
            int generation = SubProtocol.KmhDispatcher.SessionGeneration;
            Action<Action> queue = QueueForTest ?? LongEventHandler.ExecuteWhenFinished;
            queue(() =>
            {
                lock (_applyLock)
                {
                    if (generation != SubProtocol.KmhDispatcher.SessionGeneration || !IsApplied || RecoveryRequired) return;
                    try
                    {
                        if (ManagedFilesMatch()) return;
                        KmhLog.Warn("Enforcement: managed config drift requires a verified repair transaction.");
                        ApplyProfile(_state.ActiveProfileHash, DateTime.UtcNow.Ticks);
                    }
                    catch (Exception ex) { RecoveryRequired = true; EnforcementAdmission.Fail("Config repair failed: " + ex.Message); }
                }
            });
        }

        // Staged whole so a failure never leaves Config part server, part player; only the managed set is staged.
        private static int BuildCandidate(string profileFolder, string staging, bool updateManifest, out List<string> removed)
        {
            SafeDeleteDir(staging);
            Directory.CreateDirectory(staging);

            var managed = new List<string>();
            int copied = 0;
            bool preserve = _state?.PreservePersonal ?? false;

            foreach (string file in Directory.GetFiles(profileFolder, "*", SearchOption.AllDirectories))
            {
                string rel = MakeRelative(profileFolder, file);
                if (string.IsNullOrEmpty(rel)) continue;
                if (ConfigProfileUtility.ShouldExcludeByRelPath(rel)) continue;
                string nameOnly = Path.GetFileName(file);
                if (EnforcementMods.IsSafeModConfigFile(nameOnly)) continue;
                // Re-checked: an older profile could name a nested path, and nothing outside the rule may be written.
                if (!Enforceable(rel))
                { KmhLog.Warn($"Enforcement: profile entry '{rel}' is not a managed config name - skipping."); continue; }

                string dest = ResolveUnder(staging, rel);
                if (dest == null) throw new InvalidDataException($"profile entry '{rel}' resolves outside Config");

                string personal = ResolveUnder(ConfigPath, rel);
                if (preserve && File.Exists(personal)
                    && nameOnly.StartsWith("Mod_", StringComparison.OrdinalIgnoreCase)
                    && nameOnly.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    File.WriteAllText(dest, ConfigMerge.MergePreservingPersonal(File.ReadAllText(file), File.ReadAllText(personal)));
                else
                    File.Copy(file, dest, overwrite: true);

                managed.Add(rel);
                copied++;
            }

            // Before the baseline and the commit, which record one entry per case-blind name.
            ConfigProfileUtility.RefuseCaseTwins(managed, "config profile");
            if (!CaseBlindDisk) RefuseCaseOnlyDifferences(managed);

            // Files the last profile wrote and this one does not go back to the player's originals.
            removed = new List<string>();
            var now = new HashSet<string>(managed, StringComparer.OrdinalIgnoreCase);
            foreach (string rel in _state?.ManagedFiles ?? new List<string>())
                if (!now.Contains(rel)) removed.Add(rel);

            if (updateManifest)
            {
                if (_state == null) _state = new State();
                _state.ManagedFiles = managed;
            }
            return copied;
        }

        internal static bool? CaseBlindForTest;
        private static bool CaseBlindDisk => CaseBlindForTest ?? Environment.OSVersion.Platform == PlatformID.Win32NT;

        // A case-sensitive disk keeps both spellings while the backup records one, so the player's file would go unbacked.
        private static void RefuseCaseOnlyDifferences(List<string> managed)
        {
            var exact = new HashSet<string>(StringComparer.Ordinal);
            if (Directory.Exists(ConfigPath))
                foreach (string f in Directory.GetFiles(ConfigPath, "*", SearchOption.TopDirectoryOnly)) exact.Add(Path.GetFileName(f));
            ConfigBaseline b = ConfigBaseline.Verify(BackupPath, out _);
            if (b != null) foreach (BaselineEntry e in b.Entries) exact.Add(e.Path);
            foreach (string rel in _state?.ManagedFiles ?? new List<string>()) exact.Add(rel);
            var blind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in exact) blind[n] = n;
            foreach (string rel in managed)
                if (!exact.Contains(rel) && blind.TryGetValue(rel, out string have))
                    throw new InvalidDataException($"config profile names '{rel}', but '{have}' differs from it only in letter case");
        }

        private static void RevertToBaseline(List<string> removed)
        {
            if (removed == null || removed.Count == 0) return;
            var known = new List<string>();
            foreach (string rel in removed)
            {
                if (Enforceable(rel)) { known.Add(rel); continue; }
                // Migration: an older nested entry has no baseline; deleting it is the only way to stop enforcing it.
                string legacy = ResolveUnder(ConfigPath, rel);
                if (legacy == null) { KmhLog.Warn($"Enforcement: managed entry '{rel}' is outside Config or an 8.3 alias - not deleting."); continue; }
                TryDelete(legacy);
            }
            MarkAwaitingRestart(known);
            if (known.Count > 0 && !ConfigBaseline.RestoreSubset(BackupPath, ConfigPath, known, out string why))
                throw new IOException($"{known.Count} config file(s) could not be returned to their originals: {why}");
        }

        // Contents swapped, not the directory renamed: RimWorld owns this folder. The marker covers a crash.
        private static void CommitCandidate(string staging, List<string> removed, string previousState = null)
        {
            if (!Directory.Exists(staging)) throw new InvalidOperationException("no staged config to commit");

            // The journal records only the paths this commit touches, so recovery leaves the rest of Config alone.
            SafeDeleteDir(RollbackPath);
            if (!ConfigBaseline.Write(ConfigBaseline.Capture(ConfigPath, Touched(removed)), ConfigPath, RollbackPath, out string why))
                throw new IOException("The config rollback journal could not be written: " + why);
            WriteDurable(CrashMark, previousState ?? JsonConvert.SerializeObject(_state ?? new State()));

            Directory.CreateDirectory(ConfigPath);
            foreach (string file in Directory.GetFiles(staging, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                string dest = ResolveUnder(ConfigPath, name);
                if (dest == null) throw new InvalidDataException($"staged entry '{name}' resolves outside Config");
                Persistence.KmhAtomicFile.WriteBytes(dest, File.ReadAllBytes(file));
            }
            RevertToBaseline(removed);
            SafeDeleteDir(staging);
            // The journal stays until the applied state is durable; startup can still restore the preceding state.
        }

        private static void RollBackFailedApply()
        {
            if (!File.Exists(CrashMark) || !Directory.Exists(RollbackPath)) return;
            try
            {
                var prior = JsonConvert.DeserializeObject<State>(File.ReadAllText(CrashMark));
                if (prior == null) return;
                if (!ConfigBaseline.Restore(RollbackPath, ConfigPath, out string why))
                { KmhLog.Error("Enforcement: rollback needs recovery: " + why); return; }
                _state = prior;
                SaveState();
                File.Delete(CrashMark);
                KmhLog.Warn("Enforcement: apply rollback restored the previous config transaction.");
            }
            catch (Exception ex) { KmhLog.Error("Enforcement: rollback needs recovery: " + ex.Message); }
        }

        private static string HashFile(string path) => ConfigProfileUtility.Sha256Hex(File.ReadAllBytes(path));

        private static string SafePolicy()
        {
            var values = new List<string>(EnforcementCache.SafeMods);
            values.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("\n", values);
        }

        private static bool CandidateMatchesLive(string staging, List<string> removed)
        {
            foreach (string path in Directory.GetFiles(staging, "*", SearchOption.TopDirectoryOnly))
            {
                string live = ResolveUnder(ConfigPath, Path.GetFileName(path));
                if (live == null || !File.Exists(live) || HashFile(live) != HashFile(path)) return false;
            }
            // A file this profile stops enforcing is still a change until it is back at the player's original.
            ConfigBaseline baseline = ConfigBaseline.Verify(BackupPath, out _);
            foreach (string rel in removed ?? new List<string>())
            {
                string live = ResolveUnder(ConfigPath, rel);
                if (live == null) continue;
                BaselineEntry e = baseline?.Entries.Find(x => string.Equals(x.Path, rel, StringComparison.OrdinalIgnoreCase));
                if (e == null || !e.Existed) { if (File.Exists(live)) return false; continue; }
                if (!File.Exists(live) || !string.Equals(HashFile(live), e.Hash, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static bool ManagedFilesMatch()
        {
            if (_state.ManagedHashes == null || _state.ManagedHashes.Count != _state.ManagedFiles.Count) return false;
            string folder = GetProfileFolder(_state.ActiveProfileHash);
            if (!Directory.Exists(folder)) return false;
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string source in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                string rel = MakeRelative(folder, source);
                if (ConfigProfileUtility.ShouldExcludeByRelPath(rel) || EnforcementMods.IsSafeModConfigFile(Path.GetFileName(source))
                    || !Enforceable(rel)) continue;
                expected.Add(rel);
                string live = ResolveUnder(ConfigPath, rel);
                if (live == null || !File.Exists(live)) return false;
                if (_state.PreservePersonal && Path.GetFileName(source).StartsWith("Mod_", StringComparison.OrdinalIgnoreCase)
                    && source.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    string merged = ConfigMerge.MergePreservingPersonal(File.ReadAllText(source), File.ReadAllText(live));
                    if (merged != File.ReadAllText(live)) return false;
                }
                else if (HashFile(source) != HashFile(live)) return false;
            }
            if (expected.Count != _state.ManagedFiles.Count || !expected.SetEquals(_state.ManagedFiles)) return false;
            foreach (string rel in _state.ManagedFiles)
            {
                string path = ResolveUnder(ConfigPath, rel);
                if (path == null || !File.Exists(path) || !_state.ManagedHashes.TryGetValue(rel, out string hash)
                    || hash != HashFile(path)) return false;
            }
            return true;
        }

        // The server's rule, which must read the same on both sides, less a name Windows would open as another file.
        private static bool Enforceable(string rel) => ConfigManifest.IsManagedName(rel) && !ConfigManifest.LooksLikeShortName(rel);

        // Null if it escapes the root; the trailing separator stops a sibling ("Config_old") passing the prefix test.
        private static string ResolveUnder(string rootDir, string rel)
        {
            if (string.IsNullOrWhiteSpace(rel) || string.IsNullOrWhiteSpace(rootDir)) return null;
            // Before resolving: GetFullPath can expand an 8.3 alias into the long name of the file it stands for.
            foreach (string segment in rel.Split('/', '\\'))
                if (ConfigManifest.LooksLikeShortName(segment)) return null;
            try
            {
                string root = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(Path.Combine(rootDir, rel));
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
            }
            catch { return null; }
        }

        public static bool Restore(string reason = "ExplicitUserRestore")
        {
            KmhLog.Info("Enforcement restore reason: " + reason);
            return RestoreInternal(softReload: true);
        }

        private static bool RestoreInternal(bool softReload)
        {
            lock (_applyLock)
            try
            {
                StopWatcher();
                if (ConfigBaseline.Verify(BackupPath, out string bwhy) == null && IsApplied && !File.Exists(Path.Combine(BackupPath, ConfigBaseline.ManifestName)))
                {
                    // An older build's whole-tree backup has no manifest; adopt it here too, or restore fails forever.
                    if (Directory.Exists(BackupPath))
                    {
                        if (!ConfigBaseline.EnsureCovers(ConfigPath, BackupPath, _state.ManagedFiles ?? new List<string>(), out string awhy))
                            throw new IOException("The older personal config backup could not be adopted: " + awhy);
                    }
                    else
                    {
                        // Nothing was kept to put back: the lock is released, but files still exactly the server's are
                        // listed for the player to keep or reset rather than silently becoming theirs.
                        RecordLeftovers();
                        ModsConfigJournal.RestoreOriginal(RootPath, out _);
                        ClearState();
                        try { File.Delete(CrashMark); } catch { }
                        RecoveryRequired = false;
                        KmhLog.Warn("Enforcement: a server's settings were marked applied but no backup of yours was ever kept; "
                                  + "the enforcement lock is released.");
                        return true;
                    }
                }
                if (ConfigBaseline.Verify(BackupPath, out bwhy) == null)
                {
                    if (IsApplied) throw new IOException("The personal config backup cannot be trusted: " + bwhy);
                    ClearState();
                    KmhLog.Info($"Enforcement: restore requested but there is no usable backup ({bwhy}) - cleared state.");
                    return true;
                }

                IsInternalApplyInProgress = true;
                try { RestoreBackupToConfig(softReload); }
                finally { IsInternalApplyInProgress = false; }

                if (!ModsConfigJournal.RestoreOriginal(RootPath, out string mwhy))
                    throw new IOException("Your own mod list could not be restored: " + mwhy);

                ClearState();
                File.Delete(CrashMark);
                SafeDeleteDir(BackupPath);
                KmhLog.Info($"Enforcement: restored personal configs from '{BackupPath}' and cleared enforcement.");
                RecoveryRequired = false;
                return true;
            }
            catch (Exception ex)
            {
                IsInternalApplyInProgress = false;
                KmhLog.Error($"Enforcement: restore failed: {ex}");
                RecoveryRequired = true;
                return false;
            }
        }

        // The baseline must cover every path this transaction writes, restores or deletes.
        private static List<string> Touched(List<string> removed)
        {
            var all = new List<string>(_state?.ManagedFiles ?? new List<string>());
            if (removed != null) all.AddRange(removed);
            return all;
        }

        // Fail loud: without verified originals the apply aborts rather than overwrite settings it cannot put back.
        private static void EnsureBaselineCovers(IEnumerable<string> paths)
        {
            Directory.CreateDirectory(ConfigPath);
            if (!ConfigBaseline.EnsureCovers(ConfigPath, BackupPath, paths, out string why))
                throw new IOException("The personal config baseline could not be recorded: " + why);
        }

        private static void RestoreBackupToConfig(bool softReload)
        {
            MarkAwaitingRestart(BaselinePaths(BackupPath));
            if (!ConfigBaseline.Restore(BackupPath, ConfigPath, out string why))
                throw new IOException("Personal configs could not be restored: " + why);
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
                if (!TakePendingReapply(DateTime.UtcNow)) continue;

                try { ReapplyIfActive(softReload: false); }
                catch { }
            }
        }

        // A change seen too soon after the last re-apply stays pending, so the next pass still re-checks it.
        internal static bool TakePendingReapply(DateTime nowUtc)
        {
            if (Volatile.Read(ref _pendingReapply) != 1) return false;
            if ((nowUtc - _lastReapplyUtc).TotalMilliseconds < 1000) return false;
            if (Interlocked.CompareExchange(ref _pendingReapply, 0, 1) != 1) return false;
            _lastReapplyUtc = nowUtc;
            return true;
        }

        internal static bool ReapplyPendingForTest => Volatile.Read(ref _pendingReapply) == 1;
        internal static void SetReapplyForTest(bool pending, DateTime lastUtc)
        {
            Interlocked.Exchange(ref _pendingReapply, pending ? 1 : 0);
            _lastReapplyUtc = lastUtc;
        }

        // Null when the server publishes no order, which enforces nothing.
        private static ModSyncApply.Result ApplyOrderInTransaction(List<string> order, string generation, out bool orderChanged)
        {
            orderChanged = false;
            if (order == null) return null;

            ModsConfigJournal.Begin(RootPath, generation);
            ModSyncApply.Result r = ModSyncApply.Apply(order);
            orderChanged = r.Applied && r.Changed;
            if (r.Applied && !r.Changed) ModsConfigJournal.Discard(RootPath);
            return r;
        }

        private static Mods.KmhModPlan PlanOrder(List<string> order)
            => Mods.KmhModPlan.ForOrder(order,
                   InstalledModsForTest != null ? InstalledModsForTest() : Global.KmhLocalMods.Installed());

        // Before release a reconnect must prove both the on-disk order and the durable state match this server.
        internal static bool OrderVerified(out string why)
        {
            why = "";
            List<string> order = EnforcementCache.ModOrder;
            if (order.Count == 0) return true;

            // What was written is the plan, not the raw published list, so the plan is what has to be on disk.
            Mods.KmhModPlan plan = PlanOrder(order);
            if (!plan.Ok) { why = "this server's mod order was refused: " + plan.Blocked; return false; }

            if (!ModSyncApply.ActiveModsOf(ReadModsConfigForVerify(), out System.Collections.Generic.List<string> ids, out string parseError)
                || parseError != null)
            { why = "the mod list on disk could not be read"; return false; }

            if (!ModSyncApply.SameSequence(FilterToNamed(ids, plan.Order), plan.Order))
            { why = "the mod list on disk is not in the order this server requires"; return false; }

            // The durable state must agree too, or a file that merely looks right would pass unrecorded.
            if (!string.Equals(_state?.ActiveOrderHash ?? "", EnforcementCache.OrderFingerprint(order), StringComparison.OrdinalIgnoreCase))
            { why = "the applied order does not match the one this server is asking for"; return false; }

            return true;
        }

        private static string ReadModsConfigForVerify()
        {
            try { return File.ReadAllText(ModSyncApply.ConfigPath); }
            catch { return ""; }
        }

        // Only the mods the server names are ordered; a player's own extras sit wherever RimWorld puts them.
        internal static System.Collections.Generic.List<string> FilterToNamed(
            System.Collections.Generic.IList<string> mine, System.Collections.Generic.IList<string> named)
        {
            var wanted = new System.Collections.Generic.HashSet<string>(named, StringComparer.OrdinalIgnoreCase);
            var result = new System.Collections.Generic.List<string>();
            foreach (string id in mine) if (!string.IsNullOrEmpty(id) && wanted.Contains(id)) result.Add(id);
            return result;
        }

        internal static readonly TimeSpan RestartLoopWindow = TimeSpan.FromMinutes(15);
        internal static Action RestartPromptForTest;

        // automatic: consented to in this session. A repair, or a second restart for one profile within the window, asks
        // instead, so a server cannot restart the game in a loop or mid-colony.
        private static void RestartNow(bool automatic = true)
        {
            try
            {
                if (RecoveryRequired || _state == null || _state.RestartBootId != BootId || _state.RestartConsumed) return;
                bool loop = string.Equals(_state.LastRestartHash, _state.ActiveProfileHash, StringComparison.OrdinalIgnoreCase)
                    && DateTime.TryParse(_state.LastRestartUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last)
                    && DateTime.UtcNow - last.ToUniversalTime() < RestartLoopWindow;
                if (!automatic || loop)
                {
                    _state.RestartConsumed = true;
                    SaveState();
                    KmhLog.Warn(loop ? "Enforcement: this profile already restarted RimWorld minutes ago; asking instead of restarting again."
                                     : "Enforcement: the server's settings were put back in place; asking before restarting.");
                    if (EnforcementAdmission.Pending) EnforcementAdmission.Fail("RimWorld must restart to load this server's settings.");
                    AskToRestart();
                    return;
                }
                _state.RestartConsumed = true;
                _state.LastRestartHash = _state.ActiveProfileHash;
                _state.LastRestartUtc = DateTime.UtcNow.ToString("o");
                SaveState();
                ProveDurableState();
                if (RestartForTest != null) { RestartForTest(); return; }
                Messages.Message("Applying this server's enforced mod configs - restarting RimWorld...",
                    MessageTypeDefOf.NeutralEvent, historical: false);
                GenCommandLine.Restart();
            }
            catch (Exception ex) { RecoveryRequired = true; EnforcementAdmission.Fail("Restart blocked: " + ex.Message); }
        }

        private static void AskToRestart()
        {
            if (RestartPromptForTest != null) { RestartPromptForTest(); return; }
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    Find.WindowStack?.Add(new Dialog_MessageBox(
                        UI.KmhText.Get("KMH_Enf_RestartAsk", "This server's mod settings are in place, but RimWorld has to restart before every mod reads them. Restart now?"),
                        UI.KmhText.Get("KMH_Enf_AwaitRestart_Button", "Restart RimWorld now"), () => { try { GenCommandLine.Restart(); } catch { } },
                        UI.KmhText.Get("KMH_Enf_RestartLater", "Later"), null));
                }
                catch (Exception ex) { KmhLog.Warn($"Enforcement: the restart prompt could not be shown: {ex.Message}"); }
            });
        }

        private static string BootCheckPath  => Path.Combine(RootPath, "KMH_ENFORCE_BOOTCHECK.json");
        private static string BootFailedPath => Path.Combine(RootPath, "KMH_ENFORCE_BOOTFAILED.txt");

        private sealed class BootCheck
        {
            public string Hash = "";
            public int Boots;
        }

        // Why the player's own settings were put back while RimWorld started, shown once at the menu; empty if they were not.
        internal static string StartupRestoreNote { get; private set; } = "";
        private static bool _bootArmed;

        private static void MarkBootCheck(string hash)
        {
            try { Persistence.KmhAtomicFile.Write(BootCheckPath, JsonConvert.SerializeObject(new BootCheck { Hash = hash ?? "" })); }
            catch (Exception ex) { KmhLog.Warn($"Enforcement: the start-up check could not be armed: {ex.Message}"); }
        }

        // The menu drawn in a boot that started with the check armed proves the applied settings let RimWorld start.
        internal static void MainMenuReached()
        {
            try
            {
                if (!_bootArmed || !File.Exists(BootCheckPath)) return;
                _bootArmed = false;
                File.Delete(BootCheckPath);
                TryDelete(BootFailedPath);
            }
            catch (Exception ex) { KmhLog.Debug($"Enforcement: the start-up check could not be cleared: {ex.Message}"); }
        }

        internal static bool BootFailedFor(string hash)
        {
            try { return !string.IsNullOrEmpty(hash) && File.Exists(BootFailedPath) && string.Equals(File.ReadAllText(BootFailedPath).Trim(), hash, StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        // The first start after an apply arms the check; finding it still armed at the next start means that one never reached the menu.
        private static void CheckBoot()
        {
            if (!File.Exists(BootCheckPath)) return;
            BootCheck c = null;
            try { c = JsonConvert.DeserializeObject<BootCheck>(File.ReadAllText(BootCheckPath)); } catch (Exception) { }
            if (c == null || !IsApplied || !string.Equals(c.Hash, AppliedHash, StringComparison.OrdinalIgnoreCase)) { TryDelete(BootCheckPath); return; }
            if (c.Boots < 1)
            {
                c.Boots++;
                Persistence.KmhAtomicFile.Write(BootCheckPath, JsonConvert.SerializeObject(c));
                _bootArmed = true;
                return;
            }
            KmhLog.Warn($"Enforcement: RimWorld did not reach the main menu after profile {c.Hash} was applied; putting your own settings and mod list back.");
            File.WriteAllText(BootFailedPath, c.Hash);
            TryDelete(BootCheckPath);
            if (RestoreInternal(softReload: false))
                StartupRestoreNote = UI.KmhText.Get("KMH_Enf_BootRestored", "RimWorld did not reach the main menu after the last server's mod settings were applied, so Colony Nexus put your own settings and mod list back. Restart RimWorld to load them.");
        }

        // A profile applied before Class attributes were checked could name any type; it is checked where RimWorld reads it.
        private static void RefuseUnsafeApplied()
        {
            if (!IsApplied || StartupRestoreNote.Length > 0) return;
            string why = null;
            foreach (string rel in _state.ManagedFiles ?? new List<string>())
            {
                string live = Enforceable(rel) ? ResolveUnder(ConfigPath, rel) : null;
                if (live == null || !File.Exists(live)) continue;
                why = ConfigXmlGuard.FileRefusal(live);
                if (why != null) break;
            }
            if (why == null) return;
            KmhLog.Warn($"Enforcement: the applied server settings are unsafe ({why}); putting your own back.");
            if (RestoreInternal(softReload: false))
                StartupRestoreNote = UI.KmhText.Get("KMH_Enf_UnsafeRestored", "The last server's mod settings named code a settings file never needs ({0}), so Colony Nexus put your own settings and mod list back. Restart RimWorld to load them.", UI.KmhDisplayText.Inert(why, 160));
        }

        // Only the applied profile and the one leftovers point at are kept; every other copy is the server's, not the player's.
        private static void PruneProfiles()
        {
            try
            {
                if (!Directory.Exists(ProfilesRoot)) return;
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SafeHash(_state?.ActiveProfileHash) };
                try
                {
                    if (File.Exists(LeftoversPath))
                        keep.Add(SafeHash(JsonConvert.DeserializeObject<Leftovers>(File.ReadAllText(LeftoversPath))?.ProfileHash));
                }
                catch (Exception) { return; }
                foreach (string dir in Directory.GetDirectories(ProfilesRoot))
                    if (!keep.Contains(Path.GetFileName(dir))) SafeDeleteDir(dir);
            }
            catch (Exception ex) { KmhLog.Debug($"Enforcement: old profile copies could not be pruned: {ex.Message}"); }
        }

        // The way out when the backup cannot be trusted: the lock goes, the files stay, the damaged backup is set aside.
        internal static bool ReleaseKeepingFiles(out string why)
        {
            why = "";
            lock (_applyLock)
            try
            {
                StopWatcher();
                RecordLeftovers();
                if (Directory.Exists(BackupPath))
                    Directory.Move(BackupPath, BackupPath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
                SafeDeleteDir(RollbackPath);
                TryDelete(CrashMark);
                TryDelete(BootCheckPath);
                ClearState();
                RecoveryRequired = false;
                KmhLog.Warn("Enforcement: the player released the lock and kept the current files; the unusable backup was set aside, not deleted.");
                return true;
            }
            catch (Exception ex) { why = ex.Message; KmhLog.Warn($"Enforcement: the lock could not be released: {ex.Message}"); return false; }
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

        internal enum ReloadOutcome { Succeeded, RestartRequired, Unsupported, Failed }

        private static ReloadOutcome SoftReload()
        {
            // RimWorld's ReadModSettings<T>(string, string) returns a new object; it cannot update instances mods
            // already hold or their launch-time caches, so a restart is required.
            MethodInfo m = typeof(LoadedModManager).GetMethod("ReadModSettings",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(string), typeof(string) }, null);
            return m != null && m.IsGenericMethodDefinition ? ReloadOutcome.RestartRequired : ReloadOutcome.Unsupported;
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

        private static string MakeRelative(string root, string fullPath)
        {
            string r = Path.GetFullPath(root);
            if (!r.EndsWith(Path.DirectorySeparatorChar.ToString())) r += Path.DirectorySeparatorChar;
            string f = Path.GetFullPath(fullPath);
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? f.Substring(r.Length) : "";
        }

        private static void TryDelete(string path) { if (File.Exists(path)) File.Delete(path); }
        private static void SafeDeleteDir(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }

        private static void LoadState()
        {
            try { if (File.Exists(StatePath)) _state = JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath)); }
            catch { RecoveryRequired = true; throw; }
            if (_state == null) _state = new State();
        }

        private static void SaveState()
        {
            WriteDurable(StatePath, JsonConvert.SerializeObject(_state, Formatting.Indented));
            ProveDurableState();
        }

        private static void WriteDurable(string path, string json)
        {
            Persistence.KmhAtomicFile.Write(path, json);
            StateReadbackForTest?.Invoke(path);
            // Read back, not trusted: this state decides resume vs restore, so a bad write must fail now, not next boot.
            string readback = File.ReadAllText(path);
            if (readback != json || JsonConvert.DeserializeObject<State>(readback) == null)
                throw new IOException("Durable enforcement state readback failed");
        }

        private static void ProveDurableState()
        {
            State disk = JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath));
            if (disk == null || JsonConvert.SerializeObject(disk) != JsonConvert.SerializeObject(_state))
                throw new IOException("Enforcement state differs from durable readback");
        }

        private static void ClearState()
        {
            _state = new State();
            SaveState();
            TryDelete(ActiveMarker);
        }

        private class State
        {
            public string ServerId { get; set; } = "";
            public string SafePolicy { get; set; } = "";
            public string ApplyGeneration { get; set; } = "";
            public string RestartBootId { get; set; } = "";
            public bool RestartConsumed { get; set; }
            public Dictionary<string, string> ManagedHashes { get; set; }
            public bool         IsEnforcedActive            { get; set; }
            public string       ActiveProfileHash           { get; set; } = "";
            // This client's fingerprint of the list written, never the server's hash, so a reconnect proves the list itself.
            public string       ActiveOrderHash             { get; set; } = "";
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
