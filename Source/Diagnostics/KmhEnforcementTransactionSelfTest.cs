using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using KMHPatch.Features.Enforcement;
using KMHPatch.SubProtocol;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Diagnostics
{
    internal static class KmhEnforcementTransactionSelfTest
    {
        public static List<(string, bool, string)> Run()
        {
            var results = new List<(string, bool, string)>();
            // Live self-tests must never replace the player's active transaction or trigger a restart.
            if (EnforcementProfileApplier.RootOverrideForTest != null || EnforcementProfileApplier.IsApplied)
            {
                // Said out loud: an empty list read as a suite that passed with nothing to check.
                results.Add(KmhSelfTestResult.Skipped("enforcement transactions: the transaction harness ran",
                                                      "an enforcement profile is live in this game"));
                return results;
            }
            string root = Path.Combine(Path.GetTempPath(), "kmh-enforcement-test-" + Guid.NewGuid().ToString("N"));
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var applier = typeof(EnforcementProfileApplier);
            var stateField = applier.GetField("_state", flags);
            object priorState = stateField.GetValue(null);
            string priorServer = KmhDispatcher.ServerId;
            var recovery = applier.GetField("<RecoveryRequired>k__BackingField", flags);
            object priorRecovery = recovery.GetValue(null);
            var priorOrder = new List<string>(EnforcementCache.ModOrder);
            string priorOrderHash = EnforcementCache.ModOrderHash;
            void Check(string name, bool ok)
            {
                results.Add((name, ok, ok ? "verified" : "failed"));
                if (Environment.GetEnvironmentVariable("KMH_TRACE_TESTS") == "1") Console.WriteLine(name + ": " + ok);
            }
            int restarts = 0;
            int failures = 0;
            // Built as the server's EnforcementPublish.BuildZip does, or a fixture passes where a real profile fails.
            byte[] ServerShapedZip(string dir)
            {
                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        foreach (string f in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
                        {
                            string name = Path.GetFileName(f);
                            if (!ConfigManifest.IsManagedName(name)) continue;
                            zip.CreateEntryFromFile(f, name, CompressionLevel.Optimal);
                        }
                    return ms.ToArray();
                }
            }
            try
            {
                EnforcementProfileApplier.RootOverrideForTest = root;
                EnforcementAdmission.FailureForTest = _ => failures++;
                EnforcementProfileApplier.QueueForTest = action => action();
                EnforcementProfileApplier.RestartForTest = () => restarts++;
                recovery.SetValue(null, false);
                stateField.SetValue(null, null);
                KmhDispatcher.ServerId = "transaction-test";
                Directory.CreateDirectory(Path.Combine(root, "Config"));
                Directory.CreateDirectory(Path.Combine(root, "source"));
                File.WriteAllText(Path.Combine(root, "Config", "settings.xml"), "<settings>personal</settings>");
                File.WriteAllText(Path.Combine(root, "source", "settings.xml"), "<settings>server</settings>");
                byte[] zip = ServerShapedZip(Path.Combine(root, "source"));
                var pending = new Queue<Action>();
                EnforcementProfileApplier.QueueForTest = action => pending.Enqueue(action);
                Receive(zip);
                Receive(zip);
                while (pending.Count > 0) pending.Dequeue()();
                EnforcementProfileApplier.QueueForTest = action => action();
                Check("NewProfileCausesAtMostOneRestart", restarts == 1 && EnforcementProfileApplier.IsApplied);
                Check("DuplicateQueuedProfileEndAppliesOnce", restarts == 1);
                Receive(zip);
                Check("DuplicateProfileEndCreatesOneRestartTicket", restarts == 1);
                string statePath = Path.Combine(root, "KMH_ENFORCE_STATE.json");
                var disk = JObject.Parse(File.ReadAllText(statePath));
                Check("RestartTicketIsDurablyConsumed", (bool)disk["RestartConsumed"] && !string.IsNullOrEmpty((string)disk["ApplyGeneration"]));
                string generation = (string)disk["ApplyGeneration"];
                // Model a new process loading the durable ticket; all config operations still run through production code.
                disk["RestartBootId"] = "previous-process";
                File.WriteAllText(statePath, disk.ToString());
                stateField.SetValue(null, null);
                applier.GetMethod("LoadState", flags).Invoke(null, null);
                Receive(zip);
                Check("SameProfileAfterRestartCausesZeroRestarts", restarts == 1);
                Receive(zip);
                Check("PreviousServerReconnectCausesZeroSecondRestart", restarts == 1);
                Check("SameProfileKeepsApplyGeneration", (string)JObject.Parse(File.ReadAllText(statePath))["ApplyGeneration"] == generation);
                File.WriteAllText(Path.Combine(root, "Config", "settings.xml"), "<settings>drift</settings>");
                int promptsBefore = Prompts;
                Receive(zip);
                // Repaired at once, but the restart is asked for: this profile already restarted the game minutes ago.
                Check("DriftRepairWithinTheLoopWindowAsksInsteadOfRestarting", restarts == 1 && Prompts == promptsBefore + 1
                    && File.ReadAllText(Path.Combine(root, "Config", "settings.xml")) == "<settings>server</settings>");
                File.WriteAllText(Path.Combine(root, "source", "settings.xml"), "<settings>changed</settings>");
                byte[] changed = ServerShapedZip(Path.Combine(root, "source"));
                using (var lockedState = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    Receive(changed);
                Check("StateWriteFailurePreventsRestart", restarts == 1 && Prompts == promptsBefore + 1 && EnforcementProfileApplier.RecoveryRequired);
                Check("StateWriteFailureRollsBackFiles", File.ReadAllText(Path.Combine(root, "Config", "settings.xml")) == "<settings>server</settings>");
                applier.GetMethod("RollBackFailedApply", flags).Invoke(null, null);
                recovery.SetValue(null, false);
                EnforcementProfileApplier.StateReadbackForTest = path => { if (path == statePath) throw new IOException("injected readback failure"); };
                Receive(changed);
                Check("StateReadbackFailurePreventsRestart", restarts == 1 && Prompts == promptsBefore + 1 && EnforcementProfileApplier.RecoveryRequired);
                Check("ApplyRollbackRestoresSafely", File.ReadAllText(Path.Combine(root, "Config", "settings.xml")) == "<settings>server</settings>");
                EnforcementProfileApplier.StateReadbackForTest = null;
                applier.GetMethod("RollBackFailedApply", flags).Invoke(null, null);
                recovery.SetValue(null, false);
                Check("ExplicitUserRestoreRestores", EnforcementProfileApplier.Restore("ExplicitUserRestore")
                    && !EnforcementProfileApplier.IsApplied
                    && File.ReadAllText(Path.Combine(root, "Config", "settings.xml")) == "<settings>personal</settings>");
                // An apply must touch only the managed set: Config can hold 100MB of another launcher's cache.
                string cfg = Path.Combine(root, "Config");
                string cache = Path.Combine(cfg, "ModsCache");
                Directory.CreateDirectory(cache);
                File.WriteAllText(Path.Combine(cfg, "ModsConfig.xml"), "<personal-order/>");
                File.WriteAllText(Path.Combine(cache, "huge.bin"), new string('x', 4096));
                File.WriteAllText(Path.Combine(cfg, "Mod_Alpha.xml"), "<a>personal</a>");

                string srcA = Path.Combine(root, "srcA");
                Directory.CreateDirectory(srcA);
                File.WriteAllText(Path.Combine(srcA, "Mod_Alpha.xml"), "<a>serverA</a>");
                string srcB = Path.Combine(root, "srcB");
                Directory.CreateDirectory(srcB);
                File.WriteAllText(Path.Combine(srcB, "Mod_Beta.xml"), "<b>serverB</b>");

                KmhDispatcher.ServerId = "server-a";
                Receive(ServerShapedZip(srcA));
                Check("ServerAProfileApplies", Text(Path.Combine(cfg, "Mod_Alpha.xml")) == "<a>serverA</a>");
                Check("ApplyLeavesUnmanagedFilesAlone",
                    Text(Path.Combine(cfg, "ModsConfig.xml")) == "<personal-order/>"
                    && File.Exists(Path.Combine(cache, "huge.bin")));

                string backup = Path.Combine(root, "ConfigKMH_Originals");
                Check("BackupHoldsOnlyTheManagedSet",
                    Directory.GetDirectories(backup).Length == 0
                    && Directory.GetFiles(backup).Length == 2                       // Mod_Alpha.xml + the manifest
                    && File.Exists(Path.Combine(backup, ConfigBaseline.ManifestName)));

                string mark = Path.Combine(root, "KMH_ENFORCE_APPLYING");
                var savedState = JObject.Parse(File.ReadAllText(statePath));
                savedState["IsEnforcedActive"] = false;                              // keeps Bootstrap off the patch installer
                File.WriteAllText(statePath, savedState.ToString());
                File.WriteAllText(mark, "{}");
                applier.GetField("_booted", flags).SetValue(null, false);
                EnforcementProfileApplier.Bootstrap();
                Check("InterruptedApplyReplaysItsJournal",
                    !File.Exists(mark)
                    && Text(Path.Combine(cfg, "Mod_Alpha.xml")) == "<a>personal</a>"
                    && !EnforcementProfileApplier.RecoveryRequired);
                Check("JournalReplayLeavesUnmanagedFilesAlone",
                    Text(Path.Combine(cfg, "ModsConfig.xml")) == "<personal-order/>"
                    && File.Exists(Path.Combine(cache, "huge.bin")));

                Receive(ServerShapedZip(srcA));
                KmhDispatcher.ServerId = "server-b";
                Receive(ServerShapedZip(srcB));
                Check("ServerBProfileApplies", Text(Path.Combine(cfg, "Mod_Beta.xml")) == "<b>serverB</b>");
                Check("FileServerBDoesNotEnforceReturnsToThePlayersOriginal",
                    Text(Path.Combine(cfg, "Mod_Alpha.xml")) == "<a>personal</a>");

                var recorded = ConfigBaseline.Verify(backup, out string vwhy);
                Check("ServerSwitchDoesNotOverwriteTheTrueBaseline",
                    recorded != null
                    && recorded.Entries.Find(e => e.Path == "Mod_Alpha.xml")?.Hash
                       == ConfigProfileUtility.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("<a>personal</a>"))
                    && recorded.Entries.Find(e => e.Path == "Mod_Beta.xml")?.Existed == false);

                Check("RestoreReturnsOriginalsAndRemovesWhatEnforcementCreated",
                    EnforcementProfileApplier.Restore("ScopeTest")
                    && Text(Path.Combine(cfg, "Mod_Alpha.xml")) == "<a>personal</a>"
                    && !File.Exists(Path.Combine(cfg, "Mod_Beta.xml"))
                    && Text(Path.Combine(cfg, "ModsConfig.xml")) == "<personal-order/>"
                    && File.Exists(Path.Combine(cache, "huge.bin")));

                // An older build's backup has no manifest: restore must adopt it as apply does, or it fails every minute.
                Receive(ServerShapedZip(srcA));
                File.Delete(Path.Combine(backup, ConfigBaseline.ManifestName));
                Check("RestoreAdoptsAnOlderBackupWithoutAManifest",
                    EnforcementProfileApplier.Restore("LegacyBackupTest") && !EnforcementProfileApplier.IsApplied
                    && !EnforcementProfileApplier.RecoveryRequired && Text(Path.Combine(cfg, "Mod_Alpha.xml")) == "<a>personal</a>");
                Receive(ServerShapedZip(srcA));
                Directory.Delete(backup, true);
                Check("RestoreWithNoBackupKeepsTheSettingsAndReleasesTheLock",
                    EnforcementProfileApplier.Restore("NoBackupTest") && !EnforcementProfileApplier.IsApplied
                    && !EnforcementProfileApplier.RecoveryRequired && Text(Path.Combine(cfg, "Mod_Alpha.xml")) == "<a>serverA</a>");
                File.WriteAllText(Path.Combine(cfg, "Mod_Alpha.xml"), "<a>personal</a>");

                Check("PersistenceFailuresRefuseAdmission", failures >= 2);

                // The player's file carries a BOM and CRLF, which a text round-trip loses while still reading equal.
                string lastFailure = "";
                EnforcementAdmission.FailureForTest = why => { failures++; lastFailure = why ?? ""; };
                void Checked(string name, bool ok, string detail) => results.Add((name, ok, ok ? "verified" : detail));
                string modsPath = Path.Combine(cfg, "ModsConfig.xml");
                string journalPath = Path.Combine(root, "KMH_MODSCONFIG_JOURNAL.json");
                string originalPath = Path.Combine(root, "KMH_MODSCONFIG_ORIGINAL.json");
                string crashMarkPath = Path.Combine(root, "KMH_ENFORCE_APPLYING");
                string nl = new string(new[] { (char)13, (char)10 });
                byte[] PlayerList(params string[] ids)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>").Append(nl).Append("<ModsConfigData>").Append(nl)
                      .Append("  <version>1.6.4871 rev1234</version>").Append(nl).Append("  <activeMods>").Append(nl);
                    foreach (string id in ids) sb.Append("    <li>").Append(id).Append("</li>").Append(nl);
                    sb.Append("  </activeMods>").Append(nl).Append("  <knownExpansions>").Append(nl)
                      .Append("    <li>ludeon.rimworld.royalty</li>").Append(nl).Append("  </knownExpansions>").Append(nl)
                      .Append("</ModsConfigData>").Append(nl);
                    byte[] body = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                    byte[] all = new byte[body.Length + 3];
                    all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
                    Buffer.BlockCopy(body, 0, all, 3, body.Length);
                    return all;
                }
                byte[] Bytes(string p) => File.Exists(p) ? File.ReadAllBytes(p) : new byte[0];
                bool Same(byte[] a, byte[] b)
                {
                    if (a.Length != b.Length) return false;
                    for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                    return true;
                }
                string Active()
                {
                    ModSyncApply.ActiveModsOf(Text(modsPath), out List<string> ids, out _);
                    return string.Join(",", ids.ToArray());
                }
                void SetOrder(string hash, params string[] ids)
                    => EnforcementCache.Apply(false, false, false, false, false, null, "", new List<string>(ids), hash);
                void Reboot(bool dropActive)
                {
                    var saved = JObject.Parse(File.ReadAllText(statePath));
                    if (dropActive) saved["IsEnforcedActive"] = false;          // keeps Bootstrap off the patch installer
                    File.WriteAllText(statePath, saved.ToString());
                    applier.GetField("_booted", flags).SetValue(null, false);
                    EnforcementProfileApplier.Bootstrap();
                    EnforcementProfileApplier.MainMenuReached();   // a reboot that got to the menu, as a real one does
                }

                ModSyncApply.ResetForTest();
                ModSyncApply.PathOverrideForTest = modsPath;
                EnforcementProfileApplier.InstalledModsForTest = () => new List<Features.Mods.InstalledMod>
                {
                    new Features.Mods.InstalledMod { Name = "Harmony", PackageId = "brrainz.harmony", Active = true },
                    new Features.Mods.InstalledMod { Name = "Core", PackageId = "ludeon.rimworld", Active = true },
                    new Features.Mods.InstalledMod { Name = "RimWorld Together", PackageId = "nova.rimworldtogether", Active = true },
                    new Features.Mods.InstalledMod { Name = "KMH: Colony Nexus", PackageId = "knappe.kmh.patch", Active = true },
                    new Features.Mods.InstalledMod { Name = "Own", PackageId = "player.own", Active = true },
                    new Features.Mods.InstalledMod { Name = "Server Mod", PackageId = "server.mod", Active = false },
                };
                byte[] mine = PlayerList("brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch", "player.own");
                File.WriteAllBytes(modsPath, mine);
                string srcM = Path.Combine(root, "srcM");
                Directory.CreateDirectory(srcM);
                File.WriteAllText(Path.Combine(srcM, "Mod_Gamma.xml"), "<g>server</g>");
                byte[] zipM = ServerShapedZip(srcM);
                KmhDispatcher.ServerId = "server-m";

                // A list without the mods it takes to undo it is refused before anything is written.
                string Refused(string hash, params string[] order)
                {
                    SetOrder(hash, order);
                    int f = failures, rs = restarts;
                    ReceiveQuietly(zipM);
                    if (failures != f + 1 || restarts != rs) return $"failures {f}->{failures}, restarts {rs}->{restarts}";
                    if (!lastFailure.Contains("Mod order refused")) return "refused for another reason: " + lastFailure;
                    if (!Same(Bytes(modsPath), mine)) return "ModsConfig.xml was written: " + Active();
                    if (File.Exists(Path.Combine(cfg, "Mod_Gamma.xml"))) return "the settings half was applied";
                    if (EnforcementProfileApplier.RecoveryRequired) return "a refusal left recovery required";
                    return File.Exists(journalPath) ? "a journal was left behind" : "";
                }
                string noKmh = Refused("H-nokmh", "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "server.mod");
                Checked("ModOrderWithoutKmhIsRefusedBeforeAnythingIsWritten", noKmh.Length == 0, noKmh);
                string noRwt = Refused("H-norwt", "brrainz.harmony", "ludeon.rimworld", "knappe.kmh.patch", "server.mod");
                Checked("ModOrderWithoutRwtIsRefusedBeforeAnythingIsWritten", noRwt.Length == 0, noRwt);
                string noCore = Refused("H-nocore", "brrainz.harmony", "nova.rimworldtogether", "knappe.kmh.patch", "server.mod");
                Checked("ModOrderWithoutCoreIsRefusedBeforeAnythingIsWritten", noCore.Length == 0, noCore);

                // Only what this machine has is written; the server's sequence, Harmony ahead of Core, is kept.
                SetOrder("H-good", "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch",
                         "server.mod", "host.only.mod");
                int rsGood = restarts, fGood = failures;
                ReceiveQuietly(zipM);
                string goodList = "brrainz.harmony,ludeon.rimworld,nova.rimworldtogether,knappe.kmh.patch,server.mod";
                Checked("ModOrderWritesOnlyPackageIdsThisMachineHas",
                    restarts == rsGood + 1 && failures == fGood && Active() == goodList,
                    $"restarts {rsGood}->{restarts}, failures {fGood}->{failures} ({lastFailure}), on disk: {Active()}");
                Checked("ModOrderApplyKeepsThePlayersOwnListForRestore",
                    EnforcementProfileApplier.RestoresModList && !File.Exists(journalPath),
                    $"original={File.Exists(originalPath)} journal={File.Exists(journalPath)}");

                // What was written is proved on the next connect, so a filtered list must not read as a wrong one.
                var afterGood = JObject.Parse(File.ReadAllText(statePath));
                afterGood["RestartBootId"] = "previous-process";
                File.WriteAllText(statePath, afterGood.ToString());
                stateField.SetValue(null, null);
                applier.GetMethod("LoadState", flags).Invoke(null, null);
                int rsVerify = restarts, fVerify = failures;
                ReceiveQuietly(zipM);
                Checked("AppliedModOrderVerifiesAfterRestartWithoutReapplying",
                    restarts == rsVerify && failures == fVerify,
                    $"restarts {rsVerify}->{restarts}, failures {fVerify}->{failures} ({lastFailure})");

                // An order that is refused is never proved either, even under the hash that was applied.
                SetOrder("H-good", "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "server.mod");
                bool hostileVerified = EnforcementProfileApplier.OrderVerified(out string hostileWhy);
                Checked("RefusedOrderIsNeverVerifiedUnderAnAppliedHash", !hostileVerified && hostileWhy.Contains("refused"),
                    hostileVerified ? "verified" : hostileWhy);

                // The player edits their list at the menu, then the state write fails after ModSync has run.
                byte[] mine2 = PlayerList("brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch",
                                          "server.mod", "player.second");
                File.WriteAllBytes(modsPath, mine2);
                File.WriteAllText(Path.Combine(srcM, "Mod_Gamma.xml"), "<g>server v2</g>");
                byte[] zipM2 = ServerShapedZip(srcM);
                SetOrder("H-good2", "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch", "server.mod");
                int rsLocked = restarts;
                using (var lockedState = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    ReceiveQuietly(zipM2);
                Checked("StateWriteFailureAfterModSyncRestoresTheExactModList",
                    restarts == rsLocked && EnforcementProfileApplier.RecoveryRequired
                    && Same(Bytes(modsPath), mine2) && !File.Exists(journalPath),
                    $"restarts {rsLocked}->{restarts}, recovery={EnforcementProfileApplier.RecoveryRequired}, "
                    + $"exact={Same(Bytes(modsPath), mine2)}, journal={File.Exists(journalPath)}, on disk: {Active()}");
                Checked("StateWriteFailureAfterModSyncStillRollsBackConfig",
                    Text(Path.Combine(cfg, "Mod_Gamma.xml")) == "<g>server</g>", Text(Path.Combine(cfg, "Mod_Gamma.xml")));
                applier.GetMethod("RollBackFailedApply", flags).Invoke(null, null);
                recovery.SetValue(null, false);

                // ModSync's own copy is a text round-trip; the journal is what brings back the exact bytes.
                ModSyncApply.ReadBackOverrideForTest = _ => "<not what was written/>";
                SetOrder("H-good2b", "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch", "server.mod");
                int fReadBack = failures;
                ReceiveQuietly(zipM);
                ModSyncApply.ReadBackOverrideForTest = null;
                Checked("ModSyncFailureInsideTheApplyRestoresTheExactModList",
                    failures == fReadBack + 1 && Same(Bytes(modsPath), mine2) && !File.Exists(journalPath)
                    && !EnforcementProfileApplier.RecoveryRequired,
                    $"failures {fReadBack}->{failures} ({lastFailure}), exact={Same(Bytes(modsPath), mine2)}, "
                    + $"journal={File.Exists(journalPath)}, recovery={EnforcementProfileApplier.RecoveryRequired}");

                // The process dies after the state is durable but before the journal is cleared: that apply stands.
                string journalAtCommit = null;
                EnforcementProfileApplier.StateReadbackForTest = path =>
                {
                    if (path == statePath && File.Exists(journalPath)) journalAtCommit = File.ReadAllText(journalPath);
                };
                SetOrder("H-good3", "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch", "server.mod");
                ReceiveQuietly(zipM);
                EnforcementProfileApplier.StateReadbackForTest = null;
                string list3 = Active();
                if (journalAtCommit != null) File.WriteAllText(journalPath, journalAtCommit);
                Reboot(dropActive: true);
                Checked("CrashAfterDurableStateKeepsTheAppliedModList",
                    journalAtCommit != null && list3 == goodList && Active() == goodList && !File.Exists(journalPath)
                    && !EnforcementProfileApplier.RecoveryRequired,
                    $"journal captured={journalAtCommit != null}, before {list3}, after {Active()}, "
                    + $"journal left={File.Exists(journalPath)}, recovery={EnforcementProfileApplier.RecoveryRequired}");

                // The process dies between the mod list write and the durable state: the next launch undoes both.
                byte[] beforeCrash = Bytes(modsPath);
                byte[] modsAtCrash = null;
                string journalAtCrash = null, stateAtCrash = null, markAtCrash = null, originalAtCrash = null;
                ModSyncApply.AfterReplaceForTest = p =>
                {
                    modsAtCrash = Bytes(modsPath);
                    journalAtCrash = File.Exists(journalPath) ? File.ReadAllText(journalPath) : null;
                    stateAtCrash = File.ReadAllText(statePath);
                    markAtCrash = File.Exists(crashMarkPath) ? File.ReadAllText(crashMarkPath) : null;
                    originalAtCrash = File.Exists(originalPath) ? File.ReadAllText(originalPath) : null;
                };
                SetOrder("H-good4", "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch");
                ReceiveQuietly(zipM);
                ModSyncApply.AfterReplaceForTest = null;
                bool captured = modsAtCrash != null && journalAtCrash != null && stateAtCrash != null;
                if (captured)
                {
                    File.WriteAllBytes(modsPath, modsAtCrash);
                    File.WriteAllText(journalPath, journalAtCrash);
                    File.WriteAllText(statePath, stateAtCrash);
                    if (markAtCrash != null) File.WriteAllText(crashMarkPath, markAtCrash);
                    if (originalAtCrash != null) File.WriteAllText(originalPath, originalAtCrash);
                }
                Reboot(dropActive: true);
                Checked("CrashBeforeDurableStateRestoresTheExactModList",
                    captured && !Same(modsAtCrash, beforeCrash) && Same(Bytes(modsPath), beforeCrash)
                    && !File.Exists(journalPath) && !EnforcementProfileApplier.RecoveryRequired,
                    $"captured={captured}, exact={Same(Bytes(modsPath), beforeCrash)}, on disk: {Active()}, "
                    + $"journal left={File.Exists(journalPath)}, recovery={EnforcementProfileApplier.RecoveryRequired}");

                // Restore is the button the join notice promises: the player's own list comes back, byte for byte.
                Checked("RestorePutsBackThePlayersOwnModList",
                    EnforcementProfileApplier.Restore("ModListTest") && Same(Bytes(modsPath), mine2)
                    && !File.Exists(originalPath) && !EnforcementProfileApplier.RestoresModList,
                    $"exact={Same(Bytes(modsPath), mine2)}, on disk: {Active()}, original left={File.Exists(originalPath)}");

                // A list the player has changed since KMH wrote it is theirs, and Restore leaves it alone.
                SetOrder("H-good5", "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch", "server.mod");
                ReceiveQuietly(zipM);
                byte[] mine3 = PlayerList("brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch", "player.third");
                File.WriteAllBytes(modsPath, mine3);
                Checked("RestoreKeepsAModListThePlayerChangedSince",
                    EnforcementProfileApplier.Restore("ModListTest") && Same(Bytes(modsPath), mine3) && !File.Exists(originalPath),
                    $"on disk: {Active()}, original left={File.Exists(originalPath)}");

                // Server A then server B: B found A's list, not the player's, so the player's stays the one kept.
                KmhDispatcher.ServerId = "server-a2";
                SetOrder("H-a2", "brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether", "knappe.kmh.patch", "server.mod");
                ReceiveQuietly(zipM);
                KmhDispatcher.ServerId = "server-b2";
                SetOrder("H-b2", "ludeon.rimworld", "brrainz.harmony", "nova.rimworldtogether", "knappe.kmh.patch");
                ReceiveQuietly(zipM);
                string listB = Active();
                Checked("ServerSwitchRestoresTheListFromBeforeTheFirstServer",
                    listB == "ludeon.rimworld,brrainz.harmony,nova.rimworldtogether,knappe.kmh.patch"
                    && EnforcementProfileApplier.Restore("ModListTest") && Same(Bytes(modsPath), mine3),
                    $"B wrote {listB}, after restore: {Active()}");

                // The order hash is the server's word: a new list under the applied hash is asked about again, never written.
                const string listA = "brrainz.harmony,ludeon.rimworld,nova.rimworldtogether,knappe.kmh.patch,server.mod";
                const string listO = "brrainz.harmony,ludeon.rimworld,nova.rimworldtogether,knappe.kmh.patch,player.own";
                SetOrder("H-bound", listA.Split(','));
                ReceiveQuietly(zipM);
                EnforcementFlow.Clear();
                bool sameListStands = EnforcementProfileApplier.IsApplied && EnforcementFlow.MayApply(EnforcementProfileApplier.AppliedHash);
                SetOrder("H-bound", listO.Split(','));
                bool newListAsks = !EnforcementFlow.MayApply(EnforcementProfileApplier.AppliedHash);
                int rsBound = restarts;
                Consent(zipM, false);
                Deliver(zipM);
                EnforcementProfileApplier.StopWatching();
                Checked("ANewModListUnderTheAppliedOrderHashIsAskedAboutAgain",
                    sameListStands && newListAsks && Active() == listA && restarts == rsBound,
                    $"same list stands={sameListStands}, new list asks={newListAsks}, restarts {rsBound}->{restarts}, on disk: {Active()}");

                // Consent covers the list on screen when Apply was chosen; one swapped in afterwards is never written on it.
                string gamma = Path.Combine(cfg, "Mod_Gamma.xml");
                File.WriteAllText(Path.Combine(srcM, "Mod_Gamma.xml"), "<g>server swap</g>");
                byte[] zipSwap = ServerShapedZip(srcM);
                SetOrder("H-swap", listA.Split(','));
                Consent(zipSwap, true);
                SetOrder("H-swap", listO.Split(','));
                bool swapAgreed = EnforcementFlow.ConsentedNow(ConfigProfileUtility.Sha256Hex(zipSwap));
                Deliver(zipSwap);
                EnforcementProfileApplier.StopWatching();
                Checked("AModListSwappedInAfterConsentIsNotWrittenOnThatConsent",
                    !swapAgreed && Active() == listA && Text(gamma) == "<g>server</g>",
                    $"still consented={swapAgreed}, on disk: {Active()}, settings: {Text(gamma)}");

                // A repair runs unasked, so it may write only the list already applied, never one sent since.
                MethodInfo reapply = applier.GetMethod("ReapplyIfActive", flags);
                EnforcementFlow.Clear();
                File.WriteAllText(gamma, "<g>drift</g>");
                reapply.Invoke(null, new object[] { false });
                bool heldBack = Active() == listA && Text(gamma) == "<g>drift</g>";
                SetOrder("H-swap", listA.Split(','));
                reapply.Invoke(null, new object[] { false });
                EnforcementProfileApplier.StopWatching();
                bool repaired = Active() == listA && Text(gamma) == "<g>server</g>";
                Checked("ARepairNeverWritesAModListNobodyAgreedTo", heldBack && repaired,
                    $"held back={heldBack}, repaired once the list matched={repaired}, on disk: {Active()}, settings: {Text(gamma)}");

                // A file review accepted for one list never covers another, even one the player agreed to while it was open.
                File.WriteAllText(Path.Combine(srcM, "Mod_Gamma.xml"), "<g>server review</g>");
                byte[] zipReview = ServerShapedZip(srcM);
                SetOrder("H-review", listA.Split(','));
                int reviews = 0;
                EnforcementFlow.ReviewForTest = h =>
                {
                    if (++reviews == 1)
                    {
                        SetOrder("H-review", listO.Split(','));
                        typeof(EnforcementFlow).GetField("_consentedOrder", flags).SetValue(null, EnforcementCache.ModOrderFingerprint);
                    }
                    return true;
                };
                ReceiveQuietly(zipReview);
                bool staleRefused = Active() == listA && EnforcementProfileApplier.AppliedHash != ConfigProfileUtility.Sha256Hex(zipReview);
                ReceiveQuietly(zipReview);
                EnforcementFlow.ReviewForTest = null;
                Checked("AReviewCoversOnlyTheModListItShowed", staleRefused && reviews == 2 && Active() == listO,
                    $"stale accept refused={staleRefused}, reviews={reviews}, on disk: {Active()}");

                // The review repeats what the mod list switches, since it is written in the same step.
                Features.Mods.KmhModPlan shownPlan = Features.Mods.KmhModPlan.ForOrder(new List<string>(listA.Split(',')), EnforcementProfileApplier.InstalledModsForTest());
                string review = Dialog_KMHEnforcementReview.Summary(new List<EnforcementProfileApplier.ReviewItem>(), shownPlan);
                bool listsSwitches = review.Contains(UI.KmhText.Get("KMH_Consent_WillEnable", "Switched on: {0}", "Server Mod"))
                                  && review.Contains(UI.KmhText.Get("KMH_Consent_WillDisable", "Switched off: {0}", "Own"))
                                  && KmhIlReader.CallsMadeBy("KMHPatch.Features.Enforcement.EnforcementProfileApplier", "OnProfileEnd").Contains("PlanOrder");
                Checked("TheFileReviewListsTheModsSwitchedOnAndOff", listsSwitches, review);

                // Apply elsewhere opens the consent dialog, so a list is never agreed to unseen.
                List<string> drawn = KmhIlReader.CallsMadeBy("KMHPatch.Features.Enforcement.Dialog_KMHEnforcement", "DrawClientView");
                Checked("TheEnforcementWindowsApplyButtonOpensTheConsentDialog", drawn.Contains("ShowConsent") && !drawn.Contains("ApplyNow"),
                    $"show consent={drawn.Contains("ShowConsent")}, applies directly={drawn.Contains("ApplyNow")}");
            }
            catch (Exception ex) { results.Add(("EnforcementTransactionHarness", false, ex.ToString())); }
            finally
            {
                ReleaseHooks();
                EnforcementProfileApplier.StateReadbackForTest = null;
                EnforcementProfileApplier.InstalledModsForTest = null;
                ModSyncApply.ResetForTest();
                RestoreModOrder(priorOrder, priorOrderHash);
                stateField.SetValue(null, priorState);
                recovery.SetValue(null, priorRecovery);
                KmhDispatcher.ServerId = priorServer;
                EnforcementFlow.Clear();
                // A file held by an indexer or antivirus must not throw away every result above.
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
            ShortNameAliases(results);
            RefusedBeforeCommit(results);
            ModListRecovery(results);
            LegacyConfigRecovery(results);
            ProfileBoundaries(results);
            return results;
        }

        // The playtest's real shape: RimWorld re-sorts a list on restart and renames a Workshop twin "_steam".
        private static byte[] ListXml(params string[] ids)
        {
            var sb = new System.Text.StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ModsConfigData>\r\n  <version>1.7.4977 rev1248</version>\r\n  <activeMods>\r\n");
            foreach (string id in ids) sb.Append("    <li>").Append(id).Append("</li>\r\n");
            sb.Append("  </activeMods>\r\n</ModsConfigData>\r\n");
            byte[] body = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            byte[] all = new byte[body.Length + 3];
            all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
            Buffer.BlockCopy(body, 0, all, 3, body.Length);
            return all;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void ModListRecovery(List<(string, bool, string)> results)
        {
            string root = Path.Combine(Path.GetTempPath(), "kmh-modlist-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string cfg = Path.Combine(root, "Config"), mods = Path.Combine(cfg, "ModsConfig.xml");
            string original = Path.Combine(root, "KMH_MODSCONFIG_ORIGINAL.json"), archive = Path.Combine(root, "ConfigKMH_ModLists");
            byte[] Now() => File.Exists(mods) ? File.ReadAllBytes(mods) : new byte[0];
            string Ids()
            {
                ModSyncApply.ActiveModsOf(System.Text.Encoding.UTF8.GetString(Now()).TrimStart('﻿'), out List<string> ids, out _);
                return string.Join(",", ids.ToArray());
            }
            void Set(byte[] b) => File.WriteAllBytes(mods, b);
            void ServerWrites(string gen, List<string> order)
            {
                ModsConfigJournal.Begin(root, gen);
                ModSyncApply.Result r = ModSyncApply.Apply(order);
                if (!r.Applied) throw new IOException("ModSync refused the test order: " + r.Failure);
                ModsConfigJournal.Commit(root, order);
            }
            bool Archived(string reason) => Directory.Exists(archive) && Directory.GetFiles(archive, "*-" + reason + ".json").Length > 0;
            var w1 = new List<string> { "ludeon.rimworld", "brrainz.harmony", "nova.rimworldtogether", "knappe.kmh.patch", "server.mod" };
            var w2 = new List<string> { "ludeon.rimworld", "brrainz.harmony", "nova.rimworldtogether", "knappe.kmh.patch" };
            byte[] mine = ListXml("brrainz.harmony", "ludeon.rimworld", "player.own", "nova.rimworldtogether_steam", "knappe.kmh.patch");
            byte[] w1Resorted = ListXml("brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether_steam", "knappe.kmh.patch", "server.mod");
            byte[] w2Resorted = ListXml("brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether_steam", "knappe.kmh.patch");
            try
            {
                Directory.CreateDirectory(cfg);
                ModSyncApply.ResetForTest();
                ModSyncApply.PathOverrideForTest = mods;

                Set(mine);
                ServerWrites("browser:a", w1);
                Set(w1Resorted);
                bool restored = ModsConfigJournal.RestoreOriginal(root, out string why1, out var outcome1);
                Verdict(results, "ModListRestoresAfterRimWorldReSortedWhatColonyNexusWrote",
                    restored && outcome1 == ModsConfigJournal.RestoreOutcome.Restored && SameBytes(Now(), mine),
                    $"ok={restored} ({why1}), outcome={outcome1}, now {Ids()}");

                Set(mine);
                ServerWrites("browser:a", w1);
                Set(w1Resorted);
                ServerWrites("browser:b", w2);
                Set(w2Resorted);
                bool kept = ModsConfigJournal.RestoreOriginal(root, out string why2, out var outcome2);
                Verdict(results, "ASecondServerAfterARestartKeepsThePlayersTrueList",
                    kept && outcome2 == ModsConfigJournal.RestoreOutcome.Restored && SameBytes(Now(), mine),
                    $"ok={kept} ({why2}), outcome={outcome2}, now {Ids()}");

                // What older builds left: the saved "original" is a server's list, re-sorted. RimWorld's backup is real.
                Set(w1Resorted);
                File.WriteAllText(original, Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    Generation = "", Bytes = Convert.ToBase64String(w1Resorted), Written = w1,
                }));
                byte[] rimworldBackup = ListXml("brrainz.harmony", "ludeon.rimworld", "player.own", "player.second", "nova.rimworldtogether_steam", "knappe.kmh.patch");
                File.WriteAllBytes(Path.Combine(cfg, "backup_ModsConfig.xml"), rimworldBackup);
                // The owner's real install: ModSync's own backup also held the server's list, re-sorted.
                File.WriteAllBytes(ModSyncApply.BackupPathFor(mods), w1Resorted);
                bool flagged = ModsConfigJournal.HasDamagedOriginal(root) && !ModsConfigJournal.HasOriginal(root);
                bool quarantined = ModsConfigJournal.QuarantineDamagedOriginal(root);
                var offered = ModsConfigJournal.RecoverableLists(root);
                bool offersRimWorld = offered.Exists(c => c.Source == "rimworld" && SameBytes(c.Bytes, rimworldBackup));
                bool offersServer = offered.Exists(c => ModsConfigJournal.SameMods(c.Mods, w1));
                Verdict(results, "ASavedCopyThatIsAServerListIsSetAsideAndRealListsAreOffered",
                    flagged && quarantined && !File.Exists(original) && Archived("server-list-kept-as-yours")
                    && offersRimWorld && !offersServer && SameBytes(Now(), w1Resorted),
                    $"flagged={flagged} quarantined={quarantined} original left={File.Exists(original)} "
                    + $"archived={Archived("server-list-kept-as-yours")} offers RimWorld={offersRimWorld} offers server={offersServer} now {Ids()}");
                // Once the player has moved on, ModSync's backup of that server's list is still never offered as theirs.
                Set(ListXml("brrainz.harmony", "ludeon.rimworld", "player.later", "nova.rimworldtogether_steam", "knappe.kmh.patch"));
                var later = ModsConfigJournal.RecoverableLists(root);
                Verdict(results, "AServersListIsNeverOfferedAsThePlayersOwn",
                    later.Exists(c => c.Source == "rimworld") && !later.Exists(c => ModsConfigJournal.SameMods(c.Mods, w1)),
                    "offered: " + string.Join(" | ", later.ConvertAll(c => c.Source + ":" + string.Join(",", c.Mods.ToArray())).ToArray()));
                File.Delete(Path.Combine(cfg, "backup_ModsConfig.xml"));
                File.Delete(ModSyncApply.BackupPathFor(mods));

                // The player changed their mods after a server set them: their list is kept, the saved one archived.
                Set(mine);
                ServerWrites("browser:c", w1);
                byte[] theirs = ListXml("brrainz.harmony", "ludeon.rimworld", "player.third", "nova.rimworldtogether_steam", "knappe.kmh.patch");
                Set(theirs);
                bool keptChange = ModsConfigJournal.RestoreOriginal(root, out string why3, out var outcome3);
                var afterChange = ModsConfigJournal.RecoverableLists(root);
                ModsConfigJournal.Candidate older = afterChange.Find(c => SameBytes(c.Bytes, mine));
                Verdict(results, "AListThePlayerChangedIsKeptAndTheOlderOneStaysRecoverable",
                    keptChange && outcome3 == ModsConfigJournal.RestoreOutcome.KeptYourChange && SameBytes(Now(), theirs)
                    && !File.Exists(original) && Archived("kept-your-change") && older != null,
                    $"ok={keptChange} ({why3}), outcome={outcome3}, now {Ids()}, archived={Archived("kept-your-change")}, offered={older != null}");

                bool recovered = older != null && ModsConfigJournal.RecoverList(root, older, out string why4);
                var undo = ModsConfigJournal.RecoverableLists(root);
                Verdict(results, "RecoveringAListKeepsTheOneItReplaced",
                    recovered && SameBytes(Now(), mine) && Archived("before-recovery") && undo.Exists(c => SameBytes(c.Bytes, theirs)),
                    $"recovered={recovered}, now {Ids()}, archived={Archived("before-recovery")}");

                // A server that only reorders writes the player's own set, and the saved copy is still the player's list.
                Set(mine);
                ServerWrites("browser:d", new List<string> { "ludeon.rimworld", "brrainz.harmony", "nova.rimworldtogether", "knappe.kmh.patch", "player.own" });
                Set(ListXml("brrainz.harmony", "ludeon.rimworld", "nova.rimworldtogether_steam", "knappe.kmh.patch", "player.own"));
                bool reorderedBack = ModsConfigJournal.RestoreOriginal(root, out string why5, out var outcome5);
                Verdict(results, "AServerThatOnlyReordersStillGetsThePlayersOwnListBack",
                    reorderedBack && outcome5 == ModsConfigJournal.RestoreOutcome.Restored && SameBytes(Now(), mine),
                    $"ok={reorderedBack} ({why5}), outcome={outcome5}, now {Ids()}");
            }
            catch (Exception ex) { results.Add(("ModListRecoveryHarness", false, ex.ToString())); }
            finally
            {
                ModSyncApply.ResetForTest();
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        // States older builds left on players' machines, fed through the current restore.
        private static void LegacyConfigRecovery(List<(string, bool, string)> results)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var applier = typeof(EnforcementProfileApplier);
            var stateField = applier.GetField("_state", flags);
            var recovery = applier.GetField("<RecoveryRequired>k__BackingField", flags);
            object priorState = stateField.GetValue(null);
            object priorRecovery = recovery.GetValue(null);
            string priorServer = KmhDispatcher.ServerId;
            var priorOrder = new List<string>(EnforcementCache.ModOrder);
            string priorOrderHash = EnforcementCache.ModOrderHash;
            string root = Path.Combine(Path.GetTempPath(), "kmh-legacy-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string cfg = Path.Combine(root, "Config"), backup = Path.Combine(root, "ConfigKMH_Originals");
            string alpha = Path.Combine(cfg, "Mod_Alpha_AlphaMod.xml"), beta = Path.Combine(cfg, "Mod_Beta_BetaMod.xml");
            void Reload(string stateJson)
            {
                File.WriteAllText(Path.Combine(root, "KMH_ENFORCE_STATE.json"), stateJson);
                stateField.SetValue(null, null);
                applier.GetMethod("LoadState", flags).Invoke(null, null);
            }
            try
            {
                EnforcementProfileApplier.RootOverrideForTest = root;
                EnforcementAdmission.FailureForTest = _ => { };
                EnforcementProfileApplier.QueueForTest = action => action();
                EnforcementProfileApplier.RestartForTest = () => { };
                EnforcementProfileApplier.ClearAwaitingRestartForTest();
                recovery.SetValue(null, false);
                EnforcementCache.Apply(false, false, false, false, false, null, "", new List<string>(), "");
                KmhDispatcher.ServerId = "legacy-test";

                // v1.3.0 and older: a whole-tree backup with no manifest, and a server switch that deleted Alpha outright.
                Directory.CreateDirectory(cfg);
                Directory.CreateDirectory(Path.Combine(backup, "SomeToolCache"));
                File.WriteAllText(Path.Combine(backup, "Mod_Alpha_AlphaMod.xml"), "<a>personal</a>");
                File.WriteAllText(Path.Combine(backup, "Mod_Beta_BetaMod.xml"), "<b>personal</b>");
                File.WriteAllText(Path.Combine(backup, "Prefs.xml"), "<prefs/>");
                File.WriteAllText(beta, "<b>server</b>");
                Reload("{\"IsEnforcedActive\":true,\"ActiveProfileHash\":\"legacyhash\",\"ManagedFiles\":[\"Mod_Beta_BetaMod.xml\"],"
                     + "\"LastAppliedUtcTicks\":0,\"PreservePersonal\":false}");
                bool restored = EnforcementProfileApplier.Restore("LegacyServerSwitch");
                Verdict(results, "LegacyBackupBringsBackSettingsAServerSwitchDeleted",
                    restored && !EnforcementProfileApplier.IsApplied && Text(alpha) == "<a>personal</a>" && Text(beta) == "<b>personal</b>",
                    $"ok={restored}, Alpha={Text(alpha)}, Beta={Text(beta)}, applied={EnforcementProfileApplier.IsApplied}");

                // Restored while the game runs: the server's values are still in memory, so saving them is refused.
                bool blocked = !Features.Enforcement.Patches.ModSettingsWriteGuard.BeforeWrite(new object[] { "Beta", "BetaMod", null });
                bool others = Features.Enforcement.Patches.ModSettingsWriteGuard.BeforeWrite(new object[] { "Other", "OtherMod", null });
                Verdict(results, "SettingsPutBackWhileRunningCannotBeSavedOverUntilRestart",
                    blocked && others && EnforcementProfileApplier.ModAwaitsRestart("Alpha") && EnforcementProfileApplier.ModAwaitsRestart("Beta")
                    && !EnforcementProfileApplier.ModAwaitsRestart("Other") && EnforcementProfileApplier.RestoreAwaitsRestart,
                    $"Beta save blocked={blocked}, other mod saves={others}, Alpha waits={EnforcementProfileApplier.ModAwaitsRestart("Alpha")}");
                EnforcementProfileApplier.ClearAwaitingRestartForTest();

                // No backup at all: the lock goes, but files still exactly the server's are listed, not made the player's.
                string src = Path.Combine(root, "src");
                Directory.CreateDirectory(src);
                File.WriteAllText(Path.Combine(src, "Mod_Alpha_AlphaMod.xml"), "<a>server</a>");
                File.WriteAllText(Path.Combine(src, "Mod_Beta_BetaMod.xml"), "<b>server</b>");
                byte[] zip;
                using (var ms = new MemoryStream())
                {
                    using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        foreach (string f in Directory.GetFiles(src)) z.CreateEntryFromFile(f, Path.GetFileName(f));
                    zip = ms.ToArray();
                }
                ReceiveQuietly(zip);
                bool appliedServer = Text(alpha) == "<a>server</a>" && EnforcementProfileApplier.IsApplied;
                Directory.Delete(backup, true);
                bool released = EnforcementProfileApplier.Restore("NoBackupLeftovers");
                var left = EnforcementProfileApplier.ServerLeftovers();
                Verdict(results, "WithNoBackupTheServersSettingsAreListedNotSilentlyMadeYours",
                    appliedServer && released && !EnforcementProfileApplier.IsApplied && left.Count == 2,
                    $"applied={appliedServer}, released={released}, listed={string.Join(",", new List<string>(left).ToArray())}");

                File.WriteAllText(beta, "<b>edited by the player</b>");
                bool reset = EnforcementProfileApplier.ResetServerLeftovers(out int removed, out string rwhy);
                Verdict(results, "ResettingLeftoversRemovesOnlyFilesStillTheServers",
                    reset && removed == 1 && !File.Exists(alpha) && Text(beta) == "<b>edited by the player</b>"
                    && EnforcementProfileApplier.ServerLeftovers().Count == 0,
                    $"ok={reset} ({rwhy}), removed={removed}, Alpha exists={File.Exists(alpha)}, Beta={Text(beta)}");
            }
            catch (Exception ex) { results.Add(("LegacyConfigRecoveryHarness", false, ex.ToString())); }
            finally
            {
                EnforcementProfileApplier.ClearAwaitingRestartForTest();
                ReleaseHooks();
                RestoreModOrder(priorOrder, priorOrderHash);
                stateField.SetValue(null, priorState);
                recovery.SetValue(null, priorRecovery);
                KmhDispatcher.ServerId = priorServer;
                EnforcementFlow.Clear();
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        // Failing before any live write refuses this server only; RecoveryRequired would fail every enforcing server.
        private static void RefusedBeforeCommit(List<(string, bool, string)> results)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var applier = typeof(EnforcementProfileApplier);
            var stateField = applier.GetField("_state", flags);
            var recovery = applier.GetField("<RecoveryRequired>k__BackingField", flags);
            object priorState = stateField.GetValue(null);
            object priorRecovery = recovery.GetValue(null);
            string priorServer = KmhDispatcher.ServerId;
            var priorOrder = new List<string>(EnforcementCache.ModOrder);
            string priorOrderHash = EnforcementCache.ModOrderHash;
            string root = Path.Combine(Path.GetTempPath(), "kmh-refuse-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string cfg = Path.Combine(root, "Config");
            string profiles = Path.Combine(root, "ConfigKMH_PROFILES");
            int failures = 0;
            string lastFailure = "";

            byte[] Zip(params (string Name, string Body)[] entries)
            {
                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        foreach ((string name, string body) in entries)
                            using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(body);
                    return ms.ToArray();
                }
            }
            bool Recovery() => EnforcementProfileApplier.RecoveryRequired;
            string alpha = Path.Combine(cfg, "Mod_Alpha.xml");
            try
            {
                EnforcementProfileApplier.RootOverrideForTest = root;
                EnforcementAdmission.FailureForTest = why => { failures++; lastFailure = why; };
                EnforcementProfileApplier.QueueForTest = action => action();
                EnforcementProfileApplier.RestartForTest = () => { };
                recovery.SetValue(null, false);
                stateField.SetValue(null, null);
                EnforcementCache.Apply(false, false, false, false, false, null, "", new List<string>(), "");
                KmhDispatcher.ServerId = "refusal-test";
                Directory.CreateDirectory(cfg);
                File.WriteAllText(alpha, "<a>personal</a>");

                // Hash-valid bytes that are not a zip at all: extraction throws before Config is looked at.
                ReceiveQuietly(System.Text.Encoding.UTF8.GetBytes("this is not a zip archive"));
                bool leftovers = Directory.Exists(profiles)
                              && Directory.GetDirectories(profiles, "*.unpack-*").Length > 0;
                Verdict(results, "ProfileThatCannotBeUnpackedRefusesOnlyThisServer",
                    failures == 1 && !Recovery() && Text(alpha) == "<a>personal</a>" && !leftovers,
                    $"failures={failures} ({lastFailure}), recovery={Recovery()}, unpack leftovers={leftovers}");

                byte[] good = Zip(("Mod_Alpha.xml", "<a>server</a>"));
                ReceiveQuietly(good);
                Verdict(results, "LegitimateServerAppliesAfterARefusedProfile",
                    failures == 1 && !Recovery() && Text(alpha) == "<a>server</a>" && EnforcementProfileApplier.IsApplied,
                    $"failures={failures} ({lastFailure}), Mod_Alpha={Text(alpha)}");

                // One letter-case apart: two files on a case-sensitive disk, one record in the journal.
                ReceiveQuietly(Zip(("Mod_Beta.xml", "<b>one</b>"), ("mod_beta.xml", "<b>two</b>")));
                Verdict(results, "CaseTwinProfileIsRefusedBeforeAnythingIsWritten",
                    failures == 2 && !Recovery() && !File.Exists(Path.Combine(cfg, "Mod_Beta.xml"))
                    && Text(alpha) == "<a>server</a>",
                    $"failures={failures} ({lastFailure}), recovery={Recovery()}, Mod_Beta written={File.Exists(Path.Combine(cfg, "Mod_Beta.xml"))}");

                // Case twins on a case-sensitive disk can't be built on Windows, so the builder's call is asserted instead.
                bool candidateRefuses = KmhIlReader.CallsMadeBy("KMHPatch.Features.Enforcement.EnforcementProfileApplier",
                                                                "BuildCandidate").Contains("RefuseCaseTwins");
                Verdict(results, "CandidateRefusesCaseTwinsOnCaseSensitiveDisks", candidateRefuses,
                    candidateRefuses ? "verified" : "BuildCandidate no longer refuses case twins");

                // A failure after staging and the baseline but before the commit: the last apply stands.
                string statePath = Path.Combine(root, "KMH_ENFORCE_STATE.json");
                string stateBefore = Text(statePath);
                EnforcementProfileApplier.PreflightForTest = () => throw new IOException("injected preflight failure");
                ReceiveQuietly(Zip(("Mod_Alpha.xml", "<a>server v2</a>"), ("Mod_Gamma.xml", "<g>server</g>")));
                EnforcementProfileApplier.PreflightForTest = null;
                object state = stateField.GetValue(null);
                var managed = state?.GetType().GetProperty("ManagedFiles")?.GetValue(state) as System.Collections.IList;
                Verdict(results, "FailureBeforeCommitLeavesNoRecoveryAndTheLastApplyStanding",
                    failures == 3 && !Recovery() && Text(alpha) == "<a>server</a>"
                    && !File.Exists(Path.Combine(cfg, "Mod_Gamma.xml")) && Text(statePath) == stateBefore
                    && managed != null && managed.Count == 1 && EnforcementProfileApplier.IsApplied,
                    $"failures={failures} ({lastFailure}), recovery={Recovery()}, Mod_Alpha={Text(alpha)}, "
                    + $"managed={managed?.Count}, state unchanged={Text(statePath) == stateBefore}");
            }
            catch (Exception ex) { results.Add(("RefusedBeforeCommitHarness", false, ex.ToString())); }
            finally
            {
                EnforcementProfileApplier.PreflightForTest = null;
                ReleaseHooks();
                RestoreModOrder(priorOrder, priorOrderHash);
                stateField.SetValue(null, priorState);
                recovery.SetValue(null, priorRecovery);
                KmhDispatcher.ServerId = priorServer;
                EnforcementFlow.Clear();
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        // A server's profile is untrusted input: each check is one way it could reach past the player's consent.
        private static void ProfileBoundaries(List<(string, bool, string)> results)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var applier = typeof(EnforcementProfileApplier);
            var stateField = applier.GetField("_state", flags);
            var recovery = applier.GetField("<RecoveryRequired>k__BackingField", flags);
            var note = applier.GetField("<StartupRestoreNote>k__BackingField", flags);
            object priorState = stateField.GetValue(null);
            object priorRecovery = recovery.GetValue(null);
            string priorServer = KmhDispatcher.ServerId;
            var priorOrder = new List<string>(EnforcementCache.ModOrder);
            string priorOrderHash = EnforcementCache.ModOrderHash;
            string root = Path.Combine(Path.GetTempPath(), "kmh-bounds-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string cfg = Path.Combine(root, "Config"), profiles = Path.Combine(root, "ConfigKMH_PROFILES");
            string alpha = Path.Combine(cfg, "Mod_Alpha_AlphaMod.xml"), beta = Path.Combine(cfg, "Mod_Beta_BetaMod.xml");
            int failures = 0, restarts = 0;
            string lastFailure = "";
            byte[] Zip(params (string Name, string Body)[] entries)
            {
                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        foreach ((string name, string body) in entries)
                            using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(body);
                    return ms.ToArray();
                }
            }
            string Settings(string cls, string v)
                => "<?xml version=\"1.0\" encoding=\"utf-8\"?><SettingsBlock><ModSettings Class=\"" + cls + "\"><v>" + v + "</v></ModSettings></SettingsBlock>";
            string own = typeof(KMHPatchSettings).FullName;
            void Fresh()
            {
                EnforcementProfileApplier.StopWatching();
                stateField.SetValue(null, null);
                recovery.SetValue(null, false);
                note.SetValue(null, "");
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Directory.CreateDirectory(cfg);
                File.WriteAllText(alpha, Settings(own, "personal"));
            }
            void Unconsented(byte[] zip)
            {
                string hash = ConfigProfileUtility.Sha256Hex(zip);
                typeof(EnforcementFlow).GetField("<PendingHash>k__BackingField", flags).SetValue(null, hash);
                typeof(EnforcementFlow).GetField("_awaitingApply", flags).SetValue(null, false);
                EnforcementProfileApplier.OnProfileBegin(hash, 1, zip.Length);
                EnforcementProfileApplier.OnProfileChunk(hash, 0, Convert.ToBase64String(zip));
                EnforcementProfileApplier.OnProfileEnd(hash);
                EnforcementProfileApplier.StopWatching();
            }
            bool Refused(byte[] zip, string expect)
            {
                int before = failures;
                ReceiveQuietly(zip);
                return failures == before + 1 && lastFailure.IndexOf(expect, StringComparison.Ordinal) >= 0
                    && Text(alpha) == Settings(own, "personal") && !EnforcementProfileApplier.IsApplied && !EnforcementProfileApplier.RecoveryRequired;
            }
            try
            {
                EnforcementProfileApplier.RootOverrideForTest = root;
                EnforcementAdmission.FailureForTest = why => { failures++; lastFailure = why ?? ""; };
                EnforcementProfileApplier.QueueForTest = action => action();
                EnforcementProfileApplier.RestartForTest = () => restarts++;
                EnforcementCache.Apply(false, false, false, false, false, null, "", new List<string>(), "");
                KmhDispatcher.ServerId = "bounds-test";

                // F1: RimWorld builds whatever a settings file's Class names, at every launch.
                Fresh();
                bool foreign = Refused(Zip(("Mod_Alpha_AlphaMod.xml", Settings("System.Collections.ArrayList", "x"))), "not part of RimWorld or a loaded mod");
                bool qualified = Refused(Zip(("Mod_Alpha_AlphaMod.xml", Settings("Verse.Game, Assembly-CSharp", "x"))), "only code outside");
                bool notSettings = Refused(Zip(("Mod_Alpha_AlphaMod.xml", Settings("Verse.Thing", "x"))), "not a mod settings type");
                bool dtd = Refused(Zip(("Mod_Alpha_AlphaMod.xml", "<!DOCTYPE d [<!ENTITY e \"x\">]><SettingsBlock>&e;</SettingsBlock>")), "not plain settings XML");
                bool typedJson = Refused(Zip(("Mod_Json_JsonMod.json", "{\"$type\":\"System.Object, mscorlib\"}")), "names a .NET type");
                Verdict(results, "ProfileNamingCodeOutsideTheGameIsRefusedBeforeAnyWrite",
                    foreign && qualified && notSettings && dtd && typedJson,
                    $"foreign={foreign} qualified={qualified} notSettings={notSettings} dtd={dtd} json={typedJson} ({lastFailure})");

                // Newtonsoft decodes a name's escapes before it looks for $type, and stops quietly at the end of a cut-off file.
                bool escapedJson = Refused(Zip(("Mod_Json_JsonMod.json", "{\"" + (char)92 + "u0024type\":\"System.Object, mscorlib\"}")), "names a .NET type");
                bool nulJson = ConfigXmlGuard.JsonRefusal("Mod_Json_JsonMod.json", "{\"a\":\"x" + (char)0 + "y\"}") != null;
                bool cutJson = ConfigXmlGuard.JsonRefusal("Mod_Json_JsonMod.json", "{\"a\":") != null;
                bool plainJson = ConfigXmlGuard.JsonRefusal("Mod_Json_JsonMod.json", "{\"a\":1,\"b\":[\"$type\"]}") == null
                              && ConfigXmlGuard.JsonRefusal("Mod_Json_JsonMod.json", "{\"$id\":\"1\",\"a\":{\"$ref\":\"1\"}}") == null;
                Verdict(results, "AJsonSettingsFileIsReadAsNewtonsoftReadsIt", escapedJson && nulJson && cutJson && plainJson,
                    $"escaped $type={escapedJson} NUL={nulJson} cut off={cutJson} plain accepted={plainJson} ({lastFailure})");

                ReceiveQuietly(Zip(("Mod_Alpha_AlphaMod.xml", Settings(own, "server"))));
                Verdict(results, "ProfileNamingAModsOwnSettingsTypeApplies",
                    EnforcementProfileApplier.IsApplied && Text(alpha) == Settings(own, "server"), $"applied={EnforcementProfileApplier.IsApplied} ({lastFailure})");

                // An apply from before the check: the live file is re-read at start-up and the player's own come back.
                File.WriteAllText(alpha, Settings("System.Collections.ArrayList", "planted"));
                applier.GetMethod("RefuseUnsafeApplied", flags).Invoke(null, null);
                Verdict(results, "AnUnsafeProfileAlreadyAppliedIsUndoneAtStartUp",
                    !EnforcementProfileApplier.IsApplied && Text(alpha) == Settings(own, "personal") && EnforcementProfileApplier.StartupRestoreNote.Length > 0,
                    $"applied={EnforcementProfileApplier.IsApplied}, note={EnforcementProfileApplier.StartupRestoreNote.Length > 0}");

                // F2: the second consent covers the files; nothing is written without it.
                Fresh();
                EnforcementFlow.ReviewForTest = _ => false;
                byte[] reviewed = Zip(("Mod_Alpha_AlphaMod.xml", Settings(own, "server")), ("Mod_Beta_BetaMod.xml", Settings(own, "b")),
                                      ("Mod_Gone_GoneMod.xml", Settings(own, "g")));
                ReceiveQuietly(reviewed);
                bool untouched = !EnforcementProfileApplier.IsApplied && Text(alpha) == Settings(own, "personal") && !File.Exists(beta);
                Verse.ModContentPack anyMod = Verse.LoadedModManager.RunningModsListForReading.Count > 0 ? Verse.LoadedModManager.RunningModsListForReading[0] : null;
                EnforcementProfileApplier.ModForSettingsFileForTest = n => n.StartsWith("Mod_Gone_", StringComparison.Ordinal) ? null : anyMod;
                var items = EnforcementProfileApplier.DescribeProfile(ConfigProfileUtility.Sha256Hex(reviewed));
                EnforcementProfileApplier.ModForSettingsFileForTest = null;
                EnforcementProfileApplier.ReviewKind KindOf(string f) => items.Find(i => i.File == f)?.Kind ?? (EnforcementProfileApplier.ReviewKind)(-1);
                bool described = items.Count == 3 && KindOf("Mod_Alpha_AlphaMod.xml") == EnforcementProfileApplier.ReviewKind.Replaces
                    && KindOf("Mod_Beta_BetaMod.xml") == EnforcementProfileApplier.ReviewKind.Adds && KindOf("Mod_Gone_GoneMod.xml") == EnforcementProfileApplier.ReviewKind.Unused;
                Verdict(results, "DecliningTheFileReviewWritesNothingAndTheReviewNamesEachChange", untouched && described && anyMod != null,
                    $"untouched={untouched}, described={described} ({items.Count} item(s))");
                EnforcementFlow.ReviewForTest = null;

                // F3: a case-sensitive disk would hold both spellings while the backup records one.
                Fresh();
                EnforcementProfileApplier.CaseBlindForTest = false;
                bool caseRefused = Refused(Zip(("MOD_ALPHA_ALPHAMOD.xml", Settings(own, "server"))), "only in letter case");
                EnforcementProfileApplier.CaseBlindForTest = null;
                Verdict(results, "ANameOnlyLetterCaseApartFromTheConfigIsRefused", caseRefused, lastFailure);

                // F4: only flat settings names are unpacked, and copies of other profiles do not pile up.
                Fresh();
                Directory.CreateDirectory(Path.Combine(profiles, "oldprofile"));
                Directory.CreateDirectory(Path.Combine(profiles, "oldprofile.unpack-1234abcd"));
                byte[] mixed = Zip(("Mod_Alpha_AlphaMod.xml", Settings(own, "server")), ("payload.exe", "MZ"), ("sub/Mod_Beta_BetaMod.xml", Settings(own, "b")),
                                   ("Shortcut.lnk", "L"), ("desktop.ini", "x"));
                ReceiveQuietly(mixed);
                string unpacked = Path.Combine(profiles, ConfigProfileUtility.Sha256Hex(mixed));
                string[] kept = Directory.Exists(unpacked) ? Directory.GetFileSystemEntries(unpacked) : new string[0];
                string[] folders = Directory.Exists(profiles) ? Directory.GetDirectories(profiles) : new string[0];
                Verdict(results, "OnlyFlatSettingsNamesAreUnpackedAndOldProfileCopiesArePruned",
                    EnforcementProfileApplier.IsApplied && kept.Length == 1 && Path.GetFileName(kept[0]) == "Mod_Alpha_AlphaMod.xml" && folders.Length == 1,
                    $"unpacked=[{string.Join(",", Array.ConvertAll(kept, Path.GetFileName))}], profile folders={folders.Length}");

                byte[] counted = Zip(("Mod_Alpha_AlphaMod.xml", "<a/>"));
                int eocd = counted.Length - 22;
                counted[eocd + 8] = 0x60; counted[eocd + 9] = 0xEA; counted[eocd + 10] = 0x60; counted[eocd + 11] = 0xEA;   // 60000 entries
                string declaredWhy = "";
                try { ConfigProfileUtility.ExtractZipBytesToFolder(counted, Path.Combine(root, "declared")); }
                catch (InvalidDataException ex) { declaredWhy = ex.Message; }
                Verdict(results, "AZipDeclaringMoreEntriesThanAllowedIsRefusedBeforeItIsRead",
                    declaredWhy.IndexOf("declares 60000", StringComparison.Ordinal) >= 0, declaredWhy.Length > 0 ? declaredWhy : "not refused");

                // F5 + F7: a re-assert of an applied profile repairs drift but only asks to restart.
                int promptsBefore = Prompts, restartsBefore = restarts;
                byte[] applied = mixed;
                // Long after the last restart, so the loop guard is not what asks.
                object st = stateField.GetValue(null);
                st.GetType().GetProperty("LastRestartHash").SetValue(st, "");
                st.GetType().GetProperty("LastRestartUtc").SetValue(st, "");
                File.WriteAllText(alpha, Settings(own, "drifted"));
                Unconsented(applied);
                Verdict(results, "ARepairNobodyConsentedToThisSessionAsksBeforeRestarting",
                    Text(alpha) == Settings(own, "server") && restarts == restartsBefore && Prompts == promptsBefore + 1,
                    $"alpha={Text(alpha)}, restarts {restartsBefore}->{restarts}, prompts {promptsBefore}->{Prompts}");

                int failuresBefore = failures;
                Unconsented(Zip(("Mod_Beta_BetaMod.xml", Settings(own, "pushed"))));
                Verdict(results, "AProfileNobodyAskedForIsNotBufferedOrApplied",
                    !File.Exists(beta) && failures == failuresBefore && EnforcementProfileApplier.AppliedHash == ConfigProfileUtility.Sha256Hex(applied),
                    $"beta written={File.Exists(beta)}, failures {failuresBefore}->{failures}");

                // M-1: consent covered the order too, so a changed order hash is not "already applied".
                EnforcementCache.Apply(false, false, false, false, false, null, "", new List<string> { "ludeon.rimworld" }, "another-order");
                bool orderAsks = !EnforcementFlow.MayApply(EnforcementProfileApplier.AppliedHash);
                EnforcementCache.Apply(false, false, false, false, false, null, "", new List<string>(), "");
                Verdict(results, "AChangedModOrderIsNotTreatedAsAlreadyConsented",
                    orderAsks && EnforcementFlow.MayApply(EnforcementProfileApplier.AppliedHash), $"asks again={orderAsks}");

                // F6: the start after an apply arms a check; a second start without reaching the menu puts the player's back.
                MethodInfo checkBoot = applier.GetMethod("CheckBoot", flags);
                checkBoot.Invoke(null, null);
                EnforcementProfileApplier.MainMenuReached();
                checkBoot.Invoke(null, null);
                bool survivesMenu = EnforcementProfileApplier.IsApplied;
                File.WriteAllText(alpha, Settings(own, "drifted"));
                // Hashed as sent: a zip built again carries a new timestamp once the clock passes a 2-second mark.
                byte[] server2 = Zip(("Mod_Alpha_AlphaMod.xml", Settings(own, "server2")));
                ReceiveQuietly(server2);
                // Back at the menu before restarting: these settings have not been through a start yet, so that proves nothing.
                EnforcementProfileApplier.MainMenuReached();
                checkBoot.Invoke(null, null);
                checkBoot.Invoke(null, null);
                string bootHash = ConfigProfileUtility.Sha256Hex(server2);
                Verdict(results, "AStartThatNeverReachedTheMenuPutsThePlayersSettingsBack",
                    survivesMenu && !EnforcementProfileApplier.IsApplied && Text(alpha) == Settings(own, "personal")
                    && EnforcementProfileApplier.StartupRestoreNote.Length > 0 && EnforcementProfileApplier.BootFailedFor(bootHash),
                    $"survived menu={survivesMenu}, applied={EnforcementProfileApplier.IsApplied}, alpha={Text(alpha)}");
                note.SetValue(null, "");

                // F8: merging keeps personal fields without expanding a server's entities.
                string serverText = "<!DOCTYPE d [<!ENTITY e \"expanded\">]><SettingsBlock><ModSettings><windowPos>&e;</windowPos><rate>2</rate></ModSettings></SettingsBlock>";
                string merged = ConfigMerge.MergePreservingPersonal(serverText, "<SettingsBlock><ModSettings><windowPos>mine</windowPos></ModSettings></SettingsBlock>");
                Verdict(results, "MergingNeverExpandsAServersEntities", merged == serverText && merged.IndexOf("expanded</", StringComparison.Ordinal) < 0,
                    merged.Length > 160 ? merged.Substring(0, 160) : merged);

                // Field names from the live server profiles; raidCompositionMode and the display ones are made up.
                string[] gameplay = { "alphaAnimalSpawnMultiplier", "traderWealthOffsetFromTimesTraded", "currentMilianModifyScale",
                                      "offsetMultiLayerClothingAmount", "raidCompositionMode" };
                string[] personal = { "colors", "colorStorage", "disableSetupWindow", "lastLoadingModHash", "LastVersion", "loadingTimesMeasuredToMenu",
                                      "MiliraRace_ModSetting_MilianHairColorOffset", "ToolbarWindowPos", "iconScale", "UIScale", "labelOffsetX", "windowposx", "showtooltips" };
                string misread = string.Join(",", Array.FindAll(gameplay, ConfigHeuristics.IsPersonalField))
                               + " | " + string.Join(",", Array.FindAll(personal, f => !ConfigHeuristics.IsPersonalField(f)));
                Verdict(results, "AGameplayValueNamedLikeADisplaySettingIsEnforced", misread == " | ", misread);

                Fresh();
                try
                {
                    EnforcementCache.Apply(false, false, true, false, false, null, "", new List<string>(), "");
                    string Fields(string inner) => "<?xml version=\"1.0\" encoding=\"utf-8\"?><SettingsBlock><ModSettings Class=\"" + own + "\">"
                                                 + inner + "</ModSettings></SettingsBlock>";
                    string serverSide = Fields("<flagMime>True</flagMime><colors>server</colors>");
                    byte[] keeping = Zip(("Mod_Alpha_AlphaMod.xml", serverSide));
                    // What the old merge left a returning player: their own spawn rate beside their own colours.
                    File.WriteAllText(alpha, Fields("<flagMime>True</flagMime><alphaAnimalSpawnMultiplier>5</alphaAnimalSpawnMultiplier><colors>mine</colors>"));
                    int nudges = restarts + Prompts;
                    ReceiveQuietly(keeping);
                    string landed = Text(alpha);
                    Verdict(results, "APlayersOwnSpawnRateIsReplacedWhileTheirColoursStay",
                        landed.IndexOf("alphaAnimalSpawnMultiplier", StringComparison.Ordinal) < 0 && landed.IndexOf("<colors>mine</colors>", StringComparison.Ordinal) >= 0
                        && landed.IndexOf("<flagMime>True</flagMime>", StringComparison.Ordinal) >= 0 && restarts + Prompts == nudges + 1, landed);
                    Verdict(results, "ARestartForChangedSettingsNamesTheFileInTheLog",
                        EnforcementProfileApplier.LastDrift.IndexOf("Mod_Alpha_AlphaMod.xml", StringComparison.Ordinal) >= 0, EnforcementProfileApplier.LastDrift);

                    object live = stateField.GetValue(null);
                    live.GetType().GetProperty("RestartBootId").SetValue(live, "previous-process");
                    string relaid = System.Text.RegularExpressions.Regex.Replace(landed, @">\s+<", "><");
                    File.WriteAllText(alpha, relaid);
                    nudges = restarts + Prompts;
                    int failed = failures;
                    ReceiveQuietly(keeping);
                    string generation = (string)live.GetType().GetProperty("ApplyGeneration").GetValue(live);
                    ReceiveQuietly(keeping);
                    Verdict(results, "TheSameSettingsLaidOutDifferentlyNeedNoRestart",
                        relaid != landed && Text(alpha) == relaid && restarts + Prompts == nudges && failures == failed && EnforcementProfileApplier.IsApplied
                        && (string)live.GetType().GetProperty("ApplyGeneration").GetValue(live) == generation,
                        $"relaid={relaid != landed}, untouched={Text(alpha) == relaid}, nudges {nudges}->{restarts + Prompts}, failures {failed}->{failures}");

                    // Without the merge every file is the server's byte for byte, as before.
                    EnforcementCache.Apply(false, false, false, false, false, null, "", new List<string>(), "");
                    File.WriteAllText(alpha, serverSide.Replace("><", ">\r\n<"));
                    ReceiveQuietly(keeping);
                    Verdict(results, "WithoutTheMergeAFileIsPutBackByteForByte", Text(alpha) == serverSide, Text(alpha));
                }
                finally { EnforcementCache.Apply(false, false, false, false, false, null, "", new List<string>(), ""); }

                // F9: device names open a device on Windows, in any folder.
                bool devices = !ConfigManifest.IsManagedName("COM1.xml") && !ConfigManifest.IsManagedName("nul.json") && !ConfigManifest.IsManagedName("LPT9.settings.xml")
                    && !ConfigManifest.IsManagedName("Mod_A|B.xml") && ConfigManifest.IsManagedName("Mod_Compost_CompostMod.xml");
                Verdict(results, "DeviceNamesAreNeverManagedNames", devices, "a device name or a Windows-invalid name was accepted");

                // F10: a backup that cannot be trusted no longer locks the player in.
                Fresh();
                ReceiveQuietly(Zip(("Mod_Alpha_AlphaMod.xml", Settings(own, "server"))));
                string originals = Path.Combine(root, "ConfigKMH_Originals");
                File.WriteAllText(Path.Combine(originals, ConfigBaseline.ManifestName), "{ damaged");
                bool damaged = EnforcementProfileApplier.BaselineHealth(out _, out _) == EnforcementProfileApplier.BaselineState.Damaged;
                bool released = EnforcementProfileApplier.ReleaseKeepingFiles(out string releaseWhy);
                bool setAside = !Directory.Exists(originals) && Directory.GetDirectories(root, "ConfigKMH_Originals.damaged-*").Length == 1;
                Verdict(results, "ADamagedBackupCanBeSetAsideAndTheLockReleased",
                    damaged && released && setAside && !EnforcementProfileApplier.IsApplied && !EnforcementProfileApplier.RecoveryRequired
                    && Text(alpha) == Settings(own, "server") && EnforcementProfileApplier.ServerLeftovers().Count == 1,
                    $"damaged={damaged}, released={released} ({releaseWhy}), set aside={setAside}, leftovers={EnforcementProfileApplier.ServerLeftovers().Count}");
            }
            catch (Exception ex) { results.Add(("ProfileBoundariesHarness", false, ex.ToString())); }
            finally
            {
                EnforcementProfileApplier.ClearAwaitingRestartForTest();
                ReleaseHooks();
                note.SetValue(null, "");
                RestoreModOrder(priorOrder, priorOrderHash);
                stateField.SetValue(null, priorState);
                recovery.SetValue(null, priorRecovery);
                KmhDispatcher.ServerId = priorServer;
                EnforcementFlow.Clear();
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        private static void Verdict(List<(string, bool, string)> results, string name, bool ok, string detail)
            => results.Add((name, ok, ok ? "verified" : detail));

        private static void Receive(byte[] zip)
        {
            Consent(zip, true);
            Deliver(zip);
        }

        private static void ReceiveQuietly(byte[] zip) { Receive(zip); EnforcementProfileApplier.StopWatching(); }

        // agreed: the player chose Apply while the consent dialog showed the cached mod list; otherwise nobody asked.
        private static void Consent(byte[] zip, bool agreed)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            typeof(EnforcementFlow).GetField("<PendingHash>k__BackingField", flags).SetValue(null, ConfigProfileUtility.Sha256Hex(zip));
            typeof(EnforcementFlow).GetField("_awaitingApply", flags).SetValue(null, agreed);
            if (agreed) typeof(EnforcementFlow).GetField("_consentedOrder", flags).SetValue(null, EnforcementCache.ModOrderFingerprint);
        }

        // The server's push, consent left as it is.
        private static void Deliver(byte[] zip)
        {
            string hash = ConfigProfileUtility.Sha256Hex(zip);
            // The player accepts the file review unless a check says otherwise; a restart prompt is counted, never shown.
            if (EnforcementFlow.ReviewForTest == null) EnforcementFlow.ReviewForTest = _ => true;
            if (EnforcementProfileApplier.RestartPromptForTest == null) EnforcementProfileApplier.RestartPromptForTest = () => Prompts++;
            EnforcementProfileApplier.OnProfileBegin(hash, 1, zip.Length);
            EnforcementProfileApplier.OnProfileChunk(hash, 0, Convert.ToBase64String(zip));
            EnforcementProfileApplier.OnProfileEnd(hash);
        }

        // Missing is not the same as wrong, but a check that throws reports the harness instead of itself.
        private static string Text(string path) => File.Exists(path) ? File.ReadAllText(path) : "<missing>";

        private static int Prompts;

        private static void ReleaseHooks()
        {
            EnforcementProfileApplier.StopWatching();
            EnforcementAdmission.FailureForTest = null;
            EnforcementProfileApplier.RootOverrideForTest = null;
            EnforcementProfileApplier.QueueForTest = null;
            EnforcementProfileApplier.RestartForTest = null;
            EnforcementProfileApplier.RestartPromptForTest = null;
            EnforcementProfileApplier.CaseBlindForTest = null;
            EnforcementProfileApplier.ModForSettingsFileForTest = null;
            EnforcementFlow.ReviewForTest = null;
            ConfigXmlGuard.ResolveForTest = null;
            ConfigXmlGuard.AllowedAssemblyForTest = null;
        }

        private static void RestoreModOrder(List<string> priorOrder, string priorOrderHash)
            => EnforcementCache.Apply(EnforcementCache.Enabled, EnforcementCache.AdminBypass, EnforcementCache.PreservePersonal,
                EnforcementCache.IsAdmin, EnforcementCache.HasProfile, new List<string>(EnforcementCache.SafeMods),
                EnforcementCache.ServerProfileHash, priorOrder, priorOrderHash,
                EnforcementCache.ProfileRevision, EnforcementCache.ProfileFiles);

        // A Windows 8.3 alias opens the long-named file, so an entry named like one reaches a file the rules exclude.
        private static void ShortNameAliases(List<(string, bool, string)> results)
        {
            bool pure = ConfigManifest.LooksLikeShortName("MODSCO~1.XML") && ConfigManifest.LooksLikeShortName("mod_vi~12.xml")
                     && !ConfigManifest.LooksLikeShortName("Mod_My~Mod.xml") && !ConfigManifest.LooksLikeShortName("Mod_Victim.xml")
                     && !ConfigManifest.LooksLikeShortName("Mod_Tail~.xml");
            results.Add(("ShortNameAliasShapeIsRecognised", pure, pure ? "verified" : "an 8.3 alias shape was misread"));

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var applier = typeof(EnforcementProfileApplier);
            var stateField = applier.GetField("_state", flags);
            var recovery = applier.GetField("<RecoveryRequired>k__BackingField", flags);
            object priorState = stateField.GetValue(null);
            object priorRecovery = recovery.GetValue(null);
            string priorServer = KmhDispatcher.ServerId;
            var priorOrder = new List<string>(EnforcementCache.ModOrder);
            string priorOrderHash = EnforcementCache.ModOrderHash;
            const string victimName = "Mod_Victim_KMHPatchMod.xml";
            const string personal = "<victim>player</victim>";

            // The alias only exists on a volume that generates 8.3 names, and the temp folder may not be on one.
            string root = null, alias = null;
            var bases = new List<string> { Path.GetTempPath() };
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local)) bases.Add(Directory.Exists(Path.Combine(local, "Temp")) ? Path.Combine(local, "Temp") : local);
            }
            catch { }
            foreach (string b in bases)
            {
                string candidate = Path.Combine(b, "kmh-83-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    Directory.CreateDirectory(Path.Combine(candidate, "Config"));
                    File.WriteAllText(Path.Combine(candidate, "Config", victimName), personal);
                    for (int i = 1; i <= 4 && alias == null; i++)
                        if (File.Exists(Path.Combine(candidate, "Config", "MOD_VI~" + i + ".XML"))) alias = "MOD_VI~" + i + ".XML";
                }
                catch { }
                if (alias != null) { root = candidate; break; }
                try { Directory.Delete(candidate, true); } catch { }
            }
            if (root == null)
            {
                results.Add(("ShortNameAliasEntryNeverReachesTheFileItAliases", true, "no 8.3 names on these volumes - covered by the name rule"));
                results.Add(("StaleShortNameAliasInStateIsNeverDeletedThrough", true, "no 8.3 names on these volumes - covered by the name rule"));
                results.Add(("SkippedShortNameAliasIsNotReadAsDrift", true, "no 8.3 names on these volumes - covered by the name rule"));
                return;
            }

            string cfg = Path.Combine(root, "Config");
            string victim = Path.Combine(cfg, victimName);
            int failures = 0;
            try
            {
                EnforcementProfileApplier.RootOverrideForTest = root;
                EnforcementAdmission.FailureForTest = _ => failures++;
                EnforcementProfileApplier.QueueForTest = action => action();
                EnforcementProfileApplier.RestartForTest = () => { };
                recovery.SetValue(null, false);
                stateField.SetValue(null, null);
                EnforcementCache.Apply(false, false, false, false, false, null, "", new List<string>(), "");
                KmhDispatcher.ServerId = "alias-test";

                // Built by hand, as a hostile server would: the zip builder applies the name rule itself.
                byte[] hostile;
                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        using (var w = new StreamWriter(zip.CreateEntry("Mod_Gamma.xml").Open())) w.Write("<g>server</g>");
                        using (var w = new StreamWriter(zip.CreateEntry(alias).Open())) w.Write("<victim>server</victim>");
                    }
                    hostile = ms.ToArray();
                }
                ReceiveQuietly(hostile);
                bool applied = Text(Path.Combine(cfg, "Mod_Gamma.xml")) == "<g>server</g>" && failures == 0
                            && !EnforcementProfileApplier.RecoveryRequired;
                bool spared = Text(victim) == personal;
                results.Add(("ShortNameAliasEntryNeverReachesTheFileItAliases", applied && spared,
                    $"rest applied={applied}, {victimName}: {Text(victim)}, failures={failures}"));

                // Older state and baseline files may already list the alias; dropping it must not delete through it.
                string statePath = Path.Combine(root, "KMH_ENFORCE_STATE.json");
                var disk = JObject.Parse(File.ReadAllText(statePath));
                ((JArray)disk["ManagedFiles"]).Add(alias);
                ((JObject)disk["ManagedHashes"])[alias] = ConfigProfileUtility.Sha256Hex(File.ReadAllBytes(victim));
                File.WriteAllText(statePath, disk.ToString());
                string manifest = Path.Combine(root, "ConfigKMH_Originals", ConfigBaseline.ManifestName);
                var baseline = JObject.Parse(File.ReadAllText(manifest));
                ((JArray)baseline["Entries"]).Add(new JObject { ["Path"] = alias, ["Existed"] = false, ["Size"] = 0, ["Hash"] = "" });
                File.WriteAllText(manifest, baseline.ToString());
                stateField.SetValue(null, null);
                applier.GetMethod("LoadState", flags).Invoke(null, null);
                ReceiveQuietly(hostile);
                bool survived = Text(victim) == personal;
                results.Add(("StaleShortNameAliasInStateIsNeverDeletedThrough", survived,
                    $"{victimName}: {Text(victim)}, failures={failures}"));

                // The skipped entry is not drift, or every join and every Config write would re-apply the profile.
                disk = JObject.Parse(File.ReadAllText(statePath));
                disk["RestartBootId"] = "previous-process";
                File.WriteAllText(statePath, disk.ToString());
                long appliedAt = (long)disk["LastAppliedUtcTicks"];
                stateField.SetValue(null, null);
                applier.GetMethod("LoadState", flags).Invoke(null, null);
                ReceiveQuietly(hostile);
                long after = (long)JObject.Parse(File.ReadAllText(statePath))["LastAppliedUtcTicks"];
                results.Add(("SkippedShortNameAliasIsNotReadAsDrift", after == appliedAt && failures == 0,
                    after == appliedAt ? "verified" : "the profile was applied again"));
            }
            catch (Exception ex) { results.Add(("ShortNameAliasHarness", false, ex.ToString())); }
            finally
            {
                ReleaseHooks();
                RestoreModOrder(priorOrder, priorOrderHash);
                stateField.SetValue(null, priorState);
                recovery.SetValue(null, priorRecovery);
                KmhDispatcher.ServerId = priorServer;
                EnforcementFlow.Clear();
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }
    }
}
