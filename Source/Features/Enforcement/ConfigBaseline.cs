using System;
using System.Collections.Generic;
using System.IO;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;

namespace KMHPatch.Features.Enforcement
{
    internal sealed class BaselineEntry
    {
        public string Path = "";
        // False: enforcement created this file, so restoring deletes it.
        public bool   Existed;
        public long   Size;
        public string Hash = "";
    }

    // Held to the managed set: the whole Config tree can be 100MB+ of other tools' caches.
    internal sealed class ConfigBaseline
    {
        public int SchemaVersion = 1;
        public string CapturedUtc = "";
        public List<BaselineEntry> Entries = new List<BaselineEntry>();

        [JsonIgnore] public int Count => Entries.Count;

        public const string ManifestName = ".kmh-baseline.json";

        public bool Covers(string path)
        {
            foreach (BaselineEntry e in Entries)
                if (string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Includes paths the profile will write, so a file it creates is recorded as absent rather than missed.
        public static ConfigBaseline Capture(string configRoot, IEnumerable<string> paths)
        {
            ConfigBaseline b = new ConfigBaseline { CapturedUtc = DateTime.UtcNow.ToString("o") };
            b.AddMissing(configRoot, paths);
            return b;
        }

        private void AddMissing(string configRoot, IEnumerable<string> paths)
        {
            foreach (string name in paths)
            {
                if (!ConfigManifest.IsManagedName(name) || Covers(name)) continue;
                string full = Path.Combine(configRoot, name);
                bool exists = File.Exists(full);
                Entries.Add(new BaselineEntry
                {
                    Path = name,
                    Existed = exists,
                    Size = exists ? new FileInfo(full).Length : 0,
                    Hash = exists ? ConfigManifest.HashFile(full) : "",
                });
            }
            Entries.Sort((x, y) => string.CompareOrdinal(x.Path, y.Path));
        }

        // Never re-records an existing entry: after a server switch those files hold the previous server's
        // enforced settings, and recapturing them would enshrine those as the player's own.
        public static bool EnsureCovers(string configRoot, string backupDir, IEnumerable<string> paths, out string why)
        {
            why = "";
            try
            {
                ConfigBaseline b;
                if (File.Exists(Path.Combine(backupDir, ManifestName)))
                {
                    b = Verify(backupDir, out why);
                    if (b == null) return false;
                }
                else if (Directory.Exists(backupDir))
                {
                    b = AdoptLegacy(backupDir, paths, configRoot);
                    Persistence.KmhAtomicFile.Write(Path.Combine(backupDir, ManifestName),
                                                    JsonConvert.SerializeObject(b, Formatting.Indented));
                    if (Verify(backupDir, out why) == null) return false;
                    PruneUnlisted(b, backupDir);
                    KmhLog.Info($"Enforcement: adopted an older whole-tree backup as a {b.Count}-file baseline.");
                    return true;
                }
                else b = new ConfigBaseline { CapturedUtc = DateTime.UtcNow.ToString("o") };

                int before = b.Count;
                b.AddMissing(configRoot, paths);
                if (b.Count == before) return true;

                Directory.CreateDirectory(backupDir);
                foreach (BaselineEntry e in b.Entries)
                {
                    if (!e.Existed) continue;
                    string dst = Path.Combine(backupDir, e.Path);
                    if (File.Exists(dst) && HashMatches(dst, e.Hash))
                        continue;
                    File.Copy(Path.Combine(configRoot, e.Path), dst, overwrite: true);
                    if (!HashMatches(dst, e.Hash))
                    { why = $"the backup copy of '{e.Path}' did not match the original"; return false; }
                }

                // Last and atomic: the copies must be on disk before the manifest promises them.
                Persistence.KmhAtomicFile.Write(Path.Combine(backupDir, ManifestName),
                                                JsonConvert.SerializeObject(b, Formatting.Indented));
                return Verify(backupDir, out why) != null;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        // Migration: older builds copied the whole Config tree without a manifest; its top-level files are the
        // true originals, so they are adopted and the rest is pruned. A server switch there deleted the previous
        // server's files outright, so an original now missing from Config is adopted too, or it never comes back.
        private static ConfigBaseline AdoptLegacy(string backupDir, IEnumerable<string> paths, string configRoot)
        {
            var all = new List<string>(paths ?? new List<string>());
            foreach (string file in Directory.GetFiles(backupDir, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (ConfigManifest.IsManagedName(name) && !ConfigManifest.LooksLikeShortName(name)
                    && !File.Exists(Path.Combine(configRoot, name)))
                    all.Add(name);
            }
            return Capture(backupDir, all);
        }

        // Only once the manifest verifies, so a failure part-way leaves the old backup whole.
        private static void PruneUnlisted(ConfigBaseline b, string backupDir)
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(backupDir)) Directory.Delete(dir, true);
                foreach (string file in Directory.GetFiles(backupDir))
                {
                    string name = Path.GetFileName(file);
                    if (name == ManifestName || b.Covers(name)) continue;
                    File.Delete(file);
                }
            }
            catch (Exception ex) { KmhLog.Warn($"Baseline: could not prune the old whole-tree backup: {ex.Message}"); }
        }

        // Each copy is hash-verified before the manifest is written: an unverifiable baseline must never be trusted.
        public static bool Write(ConfigBaseline baseline, string configRoot, string backupDir, out string why)
        {
            why = "";
            try
            {
                string staging = backupDir + ".partial";
                SafeDeleteDir(staging);
                Directory.CreateDirectory(staging);

                foreach (BaselineEntry e in baseline.Entries)
                {
                    if (!e.Existed) continue;
                    string src = Path.Combine(configRoot, e.Path);
                    string dst = Path.Combine(staging, e.Path);
                    File.Copy(src, dst, overwrite: true);
                    if (!HashMatches(dst, e.Hash))
                    { why = $"the backup copy of '{e.Path}' did not match the original"; SafeDeleteDir(staging); return false; }
                }

                Persistence.KmhAtomicFile.Write(Path.Combine(staging, ManifestName),
                                                JsonConvert.SerializeObject(baseline, Formatting.Indented));

                // The folder appears complete or not at all, so a half-written baseline is never adopted.
                SafeDeleteDir(backupDir);
                Directory.Move(staging, backupDir);
                return Verify(backupDir, out why) != null;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        public static ConfigBaseline Verify(string backupDir, out string why)
        {
            why = "";
            try
            {
                string manifest = Path.Combine(backupDir, ManifestName);
                if (!File.Exists(manifest)) { why = "no baseline manifest"; return null; }
                ConfigBaseline b = JsonConvert.DeserializeObject<ConfigBaseline>(File.ReadAllText(manifest));
                if (b?.Entries == null) { why = "the baseline manifest is unreadable"; return null; }

                foreach (BaselineEntry e in b.Entries)
                {
                    // A manifest entry naming a path outside the flat managed set would restore outside Config.
                    if (!ConfigManifest.IsManagedName(e.Path)) { why = $"'{e.Path}' is not a managed name"; return null; }
                    if (!e.Existed) continue;
                    string copy = Path.Combine(backupDir, e.Path);
                    if (!File.Exists(copy)) { why = $"'{e.Path}' is missing from the backup"; return null; }
                    if (new FileInfo(copy).Length != e.Size) { why = $"'{e.Path}' is the wrong size in the backup"; return null; }
                    if (!HashMatches(copy, e.Hash))
                    { why = $"'{e.Path}' does not match its recorded hash"; return null; }
                }
                return b;
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        // Re-reads after restoring: otherwise a restore that silently did nothing looks identical to one that worked.
        public static bool Restore(string backupDir, string configRoot, out string why)
            => RestoreSubset(backupDir, configRoot, null, out why);

        public static bool RestoreSubset(string backupDir, string configRoot, ICollection<string> paths, out string why)
        {
            why = "";
            ConfigBaseline b = Verify(backupDir, out why);
            if (b == null) return false;

            try
            {
                Directory.CreateDirectory(configRoot);
                var wanted = new List<BaselineEntry>();
                foreach (BaselineEntry e in b.Entries)
                    if (paths == null || Contains(paths, e.Path)) wanted.Add(e);

                foreach (BaselineEntry e in wanted)
                {
                    string live = Path.Combine(configRoot, e.Path);
                    if (e.Existed)
                        Persistence.KmhAtomicFile.WriteBytes(live, File.ReadAllBytes(Path.Combine(backupDir, e.Path)));
                    else if (File.Exists(live)) File.Delete(live);
                }

                foreach (BaselineEntry e in wanted)
                {
                    string live = Path.Combine(configRoot, e.Path);
                    if (e.Existed)
                    {
                        if (!File.Exists(live)) { why = $"'{e.Path}' did not come back"; return false; }
                        if (!HashMatches(live, e.Hash))
                        { why = $"'{e.Path}' restored to different bytes"; return false; }
                    }
                    else if (File.Exists(live)) { why = $"'{e.Path}' was created by enforcement and is still there"; return false; }
                }
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        private static bool HashMatches(string file, string hash)
            => string.Equals(ConfigManifest.HashFile(file), hash, StringComparison.OrdinalIgnoreCase);

        private static bool Contains(ICollection<string> paths, string path)
        {
            foreach (string p in paths) if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void SafeDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception ex) { KmhLog.Debug($"Baseline: could not clear '{dir}': {ex.Message}"); }
        }
    }
}
