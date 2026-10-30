using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;
using Verse;

namespace KMHPatch.Features.Rwt
{
    internal enum RwtSwitchKind { Download, RestartOnly, UseWorkshop, UsePrevious }

    // A copy in Mods outranks the Workshop one (renamed "_steam"), so it is what every mod list loads.
    // Copies are only renamed beside Mods, on its drive: a move across drives fails.
    internal static class RwtCopies
    {
        internal const string MarkerFile = "KMH-installed.txt";
        private const string IncomingPrefix = ".kmh-rwt-incoming-";

        internal static string WorkshopCopyOverrideForTest;
        internal static string GameVersionFolderForTest;

        internal static string ModsRoot => RwtInstaller.ModsRoot;
        internal static string GameRoot => Path.GetDirectoryName(Path.GetFullPath(ModsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        internal static string LocalCopy => Path.Combine(ModsRoot, RwtAssetClassifier.ClientWorkshopId);
        // Beside Mods, not in it: RimWorld loads every folder under Mods and keeps whichever duplicate it lists first.
        internal static string PreviousRoot => Path.Combine(GameRoot, ".kmh-rwt-previous");
        private static string OriginalPath => Path.Combine(RwtPaths.KmhRoot, "rwt-original.json");

        internal static string NewIncoming() => Path.Combine(GameRoot, IncomingPrefix + Guid.NewGuid().ToString("N").Substring(0, 8));

        internal static bool OnModsDrive(string path)
            => string.Equals(Path.GetPathRoot(Path.GetFullPath(path)), Path.GetPathRoot(Path.GetFullPath(ModsRoot)), StringComparison.OrdinalIgnoreCase);

        private static readonly Regex VersionText = new Regex(@"(?<![0-9.])2[0-9]\.[0-9]{1,2}\.[0-9]{1,2}\.[0-9]{1,2}(?![0-9.])", RegexOptions.CultureInvariant);

        // RWT assemblies carry their release version (yy.m.d.n) as UTF-16 text; "" when none or several are found.
        internal static string VersionInAssembly(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 2) return "";
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
            // Metadata strings start on any byte, so both alignments are read.
            for (int shift = 0; shift < 2; shift++)
            {
                string text = Encoding.Unicode.GetString(bytes, shift, (bytes.Length - shift) & ~1);
                foreach (Match m in VersionText.Matches(text)) found.Add(m.Value);
            }
            if (found.Count != 1) return "";
            foreach (string v in found) return v;
            return "";
        }

        private static readonly Dictionary<string, (long Length, DateTime Written, string Version)> _seen
            = new Dictionary<string, (long, DateTime, string)>(StringComparer.OrdinalIgnoreCase);

        internal static string VersionOfCopy(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return "";
            string marked = MarkedVersion(root);
            if (marked.Length > 0) return marked;
            foreach (string dll in AssemblyCandidates(root))
            {
                try
                {
                    FileInfo fi = new FileInfo(dll);
                    if (_seen.TryGetValue(dll, out var hit) && hit.Length == fi.Length && hit.Written == fi.LastWriteTimeUtc)
                    { if (hit.Version.Length > 0) return hit.Version; continue; }
                    string v = VersionInAssembly(File.ReadAllBytes(dll));
                    _seen[dll] = (fi.Length, fi.LastWriteTimeUtc, v);
                    if (v.Length > 0) return v;
                }
                catch (Exception ex) { KmhLog.Debug($"RWT copies: could not read {dll}: {ex.GetType().Name}"); }
            }
            return "";
        }

        // Mirrors RimWorld's pick: this version's LoadFolders entry, else the newest older one; null without the file.
        // RWT puts 1.7 in "Current" and keeps an older release in "1.6", so the wrong folder misreports the version.
        internal static List<string> LoadFoldersFor(string root, string game)
        {
            string path = Path.Combine(root ?? "", "LoadFolders.xml");
            if (!File.Exists(path)) return null;
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.Load(path);
                if (doc.DocumentElement == null) return null;
                Version mine = ParseFolderVersion(game);
                System.Xml.XmlNode best = null;
                Version bestV = null;
                foreach (System.Xml.XmlNode e in doc.DocumentElement.ChildNodes)
                {
                    string n = e.NodeType == System.Xml.XmlNodeType.Element ? e.Name : "";
                    Version v = n.Length > 1 && (n[0] == 'v' || n[0] == 'V') ? ParseFolderVersion(n.Substring(1)) : null;
                    if (v == null || (mine != null && v > mine)) continue;
                    if (bestV == null || v > bestV) { best = e; bestV = v; }
                }
                if (best == null) return null;
                var folders = new List<string>();
                foreach (System.Xml.XmlNode li in best.ChildNodes)
                    if (li.NodeType == System.Xml.XmlNodeType.Element && li.Name == "li") folders.Add(li.InnerText.Trim().Trim('/', '\\'));
                return folders;
            }
            catch (Exception ex) { KmhLog.Debug($"RWT copies: could not read {path}: {ex.GetType().Name}"); return null; }
        }

        // Without LoadFolders.xml, RimWorld loads this version's folder, else the newest older one.
        internal static List<string> VersionFolders(string root, string game)
        {
            var found = new List<(Version v, string name)>();
            Version mine = ParseFolderVersion(game);
            try
            {
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(dir);
                    Version v = ParseFolderVersion(name);
                    if (v != null && (mine == null || v <= mine)) found.Add((v, name));
                }
            }
            catch (Exception) { }
            found.Sort((a, b) => b.v.CompareTo(a.v));
            var names = found.Select(f => f.name).ToList();
            if (!names.Contains(game)) names.Insert(0, game);
            return names;
        }

        private static Version ParseFolderVersion(string s)
            => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", @"^\d+\.\d+$") && Version.TryParse(s, out Version v) ? v : null;

        private static IEnumerable<string> AssemblyCandidates(string root)
        {
            string game = GameVersionFolderForTest ?? Compat.KmhGameVersion.MajorMinor;
            if (string.IsNullOrEmpty(game)) game = "1.6";
            // A later LoadFolders entry overrides an earlier one, so it is read first.
            List<string> listed = LoadFoldersFor(root, game);
            IEnumerable<string> folders = listed != null
                ? Enumerable.Reverse(listed).Select(f => Path.Combine(root, f, "Assemblies"))
                : VersionFolders(root, game).Select(v => Path.Combine(root, v, "Assemblies")).Concat(new[] { Path.Combine(root, "Assemblies") });
            foreach (string folder in folders)
                foreach (string name in new[] { "RTShared.dll", "Shared.dll", "RTClient.dll", "GameClient.dll" })
                {
                    string p = Path.Combine(folder, name);
                    if (File.Exists(p)) yield return p;
                }
        }

        internal static void WriteMarker(string root, string version, bool verified)
            => File.WriteAllText(Path.Combine(root, MarkerFile),
                $"version={RwtVersionText.Normalize(version)}\nverified={(verified ? "yes" : "no")}\ninstalled_utc={DateTime.UtcNow:o}\n");

        internal static bool IsKmhCopy(string root) => !string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, MarkerFile));

        internal static string MarkedVersion(string root)
        {
            try
            {
                string path = Path.Combine(root, MarkerFile);
                if (!File.Exists(path)) return "";
                foreach (string line in File.ReadAllLines(path))
                    if (line.StartsWith("version=", StringComparison.Ordinal)) return RwtVersionText.Normalize(line.Substring(8));
            }
            catch (Exception) { }
            return "";
        }

        internal static string WorkshopCopy()
        {
            if (WorkshopCopyOverrideForTest != null) return WorkshopCopyOverrideForTest.Length > 0 ? WorkshopCopyOverrideForTest : null;
            try
            {
                foreach (ModMetaData m in ModLister.AllInstalledMods)
                    if (m.Source == ContentSource.SteamWorkshop
                        && string.Equals(m.PackageIdNonUnique, Mods.KmhModPlan.RwtId, StringComparison.OrdinalIgnoreCase))
                        return m.RootDir?.FullName;
            }
            catch (Exception ex) { KmhLog.Debug($"RWT copies: could not list the Workshop copy: {ex.GetType().Name}"); }
            return null;
        }

        internal static string PreviousWith(string version)
        {
            if (string.IsNullOrEmpty(RwtVersionText.Normalize(version)) || !Directory.Exists(PreviousRoot)) return null;
            List<DirectoryInfo> dirs = new List<DirectoryInfo>(new DirectoryInfo(PreviousRoot).GetDirectories());
            dirs.Sort((a, b) => b.CreationTimeUtc.CompareTo(a.CreationTimeUtc));
            foreach (DirectoryInfo d in dirs)
                if (Directory.Exists(Path.Combine(d.FullName, "About")) && RwtVersionText.Same(MarkedVersion(d.FullName), version))
                    return d.FullName;
            return null;
        }

        internal static RwtSwitchKind Decide(string want, bool localExists, string localVersion, string workshopVersion, bool previousHasIt)
        {
            if (RwtVersionText.Normalize(want).Length == 0) return RwtSwitchKind.Download;
            bool local = localExists && RwtVersionText.Normalize(localVersion).Length > 0 && RwtVersionText.Same(localVersion, want);
            bool workshop = RwtVersionText.Normalize(workshopVersion).Length > 0 && RwtVersionText.Same(workshopVersion, want);
            if (local) return RwtSwitchKind.RestartOnly;
            if (workshop) return localExists ? RwtSwitchKind.UseWorkshop : RwtSwitchKind.RestartOnly;
            if (previousHasIt) return RwtSwitchKind.UsePrevious;
            return RwtSwitchKind.Download;
        }

        internal static RwtSwitchKind DecideOnDisk(string want, out string previous)
        {
            bool localExists = Directory.Exists(LocalCopy);
            previous = PreviousWith(want);
            return Decide(want, localExists, localExists ? VersionOfCopy(LocalCopy) : "", VersionOfCopy(WorkshopCopy()), previous != null);
        }

        private static string _shownWant;
        private static RwtSwitchKind _shownKind;
        private static DateTime _shownAt;

        // For a window drawn every frame: the disk is looked at again after two seconds.
        internal static RwtSwitchKind DecideForDisplay(string want)
        {
            DateTime now = DateTime.UtcNow;
            if (_shownWant == want && now - _shownAt < TimeSpan.FromSeconds(2)) return _shownKind;
            try { _shownKind = DecideOnDisk(want, out _); }
            catch (Exception) { _shownKind = RwtSwitchKind.Download; }
            _shownWant = want;
            _shownAt = now;
            return _shownKind;
        }

        internal static string NewPreviousSlot()
            => Path.Combine(PreviousRoot, $"{RwtAssetClassifier.ClientWorkshopId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}");

        internal static bool UseWorkshop(out string why)
        {
            why = "";
            if (!Directory.Exists(LocalCopy)) return true;
            RememberOriginal();
            string slot = NewPreviousSlot();
            try
            {
                Directory.CreateDirectory(PreviousRoot);
                bool players = !IsKmhCopy(LocalCopy);
                Directory.Move(LocalCopy, slot);
                if (players) NoteParkedOriginal(slot);
            }
            catch (Exception ex) { why = ex.Message; return false; }
            KmhLog.Info($"RWT: the copy in Mods was set aside ({slot}); the Steam Workshop copy loads on the next start.");
            return true;
        }

        internal static bool UsePrevious(string previous, out string why)
        {
            why = "";
            if (string.IsNullOrEmpty(previous) || !Directory.Exists(previous)) { why = "the copy set aside earlier is gone"; return false; }
            RememberOriginal();
            string slot = null;
            try
            {
                if (Directory.Exists(LocalCopy))
                {
                    slot = NewPreviousSlot();
                    bool players = !IsKmhCopy(LocalCopy);
                    Directory.Move(LocalCopy, slot);
                    if (players) NoteParkedOriginal(slot);
                }
                Directory.Move(previous, LocalCopy);
            }
            catch (Exception ex)
            {
                why = ex.Message;
                try { if (slot != null && !Directory.Exists(LocalCopy) && Directory.Exists(slot)) Directory.Move(slot, LocalCopy); }
                catch (Exception) { }
                return false;
            }
            KmhLog.Info($"RWT: {MarkedVersion(LocalCopy)} is back in Mods from {previous}; it loads on the next start.");
            return true;
        }

        // Unpacking leftovers from a run that was killed part-way; never inside Mods, so harmless, but not kept.
        internal static void SweepIncoming()
        {
            try
            {
                if (!Directory.Exists(GameRoot)) return;
                foreach (string d in Directory.GetDirectories(GameRoot, IncomingPrefix + "*"))
                    if ((DateTime.UtcNow - Directory.GetCreationTimeUtc(d)).TotalHours >= 1)
                        try { Directory.Delete(d, true); } catch (Exception) { }
            }
            catch (Exception ex) { KmhLog.Debug($"RWT copies: could not sweep unpack leftovers: {ex.GetType().Name}"); }
        }

        private sealed class Original
        {
            public bool HadLocal;
            public string ParkedAs = "";
        }

        internal static bool HasOriginal => File.Exists(OriginalPath);

        // Before KMH's first change: whether the player had a copy of their own in Mods. KMH's own copies do not count.
        internal static void RememberOriginal()
        {
            try
            {
                if (File.Exists(OriginalPath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(OriginalPath));
                bool own = Directory.Exists(LocalCopy) && !IsKmhCopy(LocalCopy);
                Persistence.KmhAtomicFile.Write(OriginalPath, JsonConvert.SerializeObject(new Original { HadLocal = own }));
            }
            catch (Exception ex) { KmhLog.Warn($"RWT: could not note the RimWorld Together setup before changing it: {ex.Message}"); }
        }

        internal static void NoteParkedOriginal(string slot)
        {
            Original o = ReadOriginal();
            if (o == null || !o.HadLocal || o.ParkedAs.Length > 0) return;
            o.ParkedAs = slot;
            try { Persistence.KmhAtomicFile.Write(OriginalPath, JsonConvert.SerializeObject(o)); }
            catch (Exception ex) { KmhLog.Warn($"RWT: could not note where your own copy was set aside ({slot}): {ex.Message}"); }
        }

        internal static string ProtectedSlot => ReadOriginal()?.ParkedAs ?? "";

        private static Original ReadOriginal()
        {
            try
            {
                if (!File.Exists(OriginalPath)) return null;
                Original o = JsonConvert.DeserializeObject<Original>(File.ReadAllText(OriginalPath));
                if (o != null && o.ParkedAs == null) o.ParkedAs = "";
                return o;
            }
            catch (Exception) { return null; }
        }

        internal static bool RestoreOriginal(out bool changed, out string why)
        {
            changed = false; why = "";
            Original o = ReadOriginal();
            if (o == null) { if (File.Exists(OriginalPath)) TryDelete(OriginalPath); return true; }
            try
            {
                if (Directory.Exists(LocalCopy) && IsKmhCopy(LocalCopy))
                {
                    Directory.CreateDirectory(PreviousRoot);
                    Directory.Move(LocalCopy, NewPreviousSlot());
                    changed = true;
                }
                if (o.HadLocal && o.ParkedAs.Length > 0 && Directory.Exists(o.ParkedAs) && !Directory.Exists(LocalCopy))
                {
                    Directory.Move(o.ParkedAs, LocalCopy);
                    changed = true;
                }
            }
            catch (Exception ex) { why = ex.Message; return false; }
            TryDelete(OriginalPath);
            if (changed) KmhLog.Info("RWT: your own RimWorld Together setup is back; it loads on the next start.");
            return true;
        }

        private static void TryDelete(string path) { try { File.Delete(path); } catch (Exception) { } }
    }
}
