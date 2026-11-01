using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Features.Global
{
    // RWT compares mods by display name, not packageId, so the browser must too or it disagrees with the real check.
    internal static class KmhLocalMods
    {
        private static List<string> _cached;

        public static IList<string> ActiveNames()
        {
            if (_cached != null) return _cached;
            List<string> names = new List<string>();
            try
            {
                foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                    if (m != null && !string.IsNullOrEmpty(m.Name)) names.Add(m.Name);
            }
            catch (Exception ex) { KmhLog.Warn($"Browser: reading the running mod list failed: {ex.Message}"); }
            // The list cannot change without a restart, so it is read once.
            _cached = names;
            return _cached;
        }

        private static List<string> _cachedIds;

        // Load order uses package ids: the server's order comes from ModsConfig.xml, and display names can collide.
        public static IList<string> ActivePackageIds()
        {
            if (_cachedIds != null) return _cachedIds;
            List<string> ids = new List<string>();
            try
            {
                foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                    if (m != null && !string.IsNullOrEmpty(m.PackageId)) ids.Add(m.PackageId);
            }
            catch (Exception ex) { KmhLog.Warn($"Browser: reading the running mod ids failed: {ex.Message}"); }
            _cachedIds = ids;
            return _cachedIds;
        }

        // What the named mods declare they need in their About.xml, limited to mods installed here.
        public static List<string> DependenciesOf(params string[] packageIds)
        {
            var deps = new List<string>();
            try
            {
                foreach (string id in packageIds)
                {
                    ModMetaData meta = ModLister.GetActiveModWithIdentifier(id, ignorePostfix: true);
                    if (meta?.Dependencies == null) continue;
                    foreach (ModDependency d in meta.Dependencies)
                    {
                        string dep = d?.packageId?.Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(dep) || deps.Contains(dep)) continue;
                        if (ModLister.GetModWithIdentifier(dep, ignorePostfix: true) != null) deps.Add(dep);
                    }
                }
            }
            catch (Exception ex) { KmhLog.Warn($"ModSync: could not read what Colony Nexus and RimWorld Together depend on: {ex.Message}"); }
            return deps;
        }

        private static Dictionary<string, string> _authors;

        // RWT's config has no author field, so only mods installed here can show one.
        public static string AuthorOf(string modName)
        {
            if (string.IsNullOrEmpty(modName)) return "";
            if (_authors == null)
            {
                _authors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                    {
                        if (m == null || string.IsNullOrEmpty(m.Name)) continue;
                        string author = m.ModMetaData?.AuthorsString;
                        if (!string.IsNullOrEmpty(author)) _authors[m.Name] = author;
                    }
                }
                catch (Exception ex) { KmhLog.Warn($"Browser: reading mod authors failed: {ex.Message}"); }
            }
            return _authors.TryGetValue(modName, out string found) ? found : "";
        }

        public static List<Mods.InstalledMod> Installed()
        {
            List<Mods.InstalledMod> list = new List<Mods.InstalledMod>();
            try
            {
                foreach (ModMetaData m in ModLister.AllInstalledMods)
                {
                    if (m == null || string.IsNullOrEmpty(m.PackageId)) continue;
                    if (_workshopFolder == null && m.OnSteamWorkshop) _workshopFolder = m.RootDir?.Parent?.FullName;
                    list.Add(new Mods.InstalledMod
                    {
                        Name       = m.Name ?? "",
                        PackageId  = m.PackageId,
                        WorkshopId = Mods.KmhWorkshop.IsWorkshopId(m.FolderName) ? m.FolderName : "",
                        Active     = m.Active,
                        Official   = m.Official,
                    });
                }
            }
            catch (Exception ex) { KmhLog.Warn($"Mods: reading the installed mod list failed: {ex.Message}"); }
            return list;
        }

        private static volatile List<Mods.InstalledMod> _installedSnapshot;
        private static volatile string _workshopFolder;
        private static readonly Dictionary<string, string> _packageOnDisk = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static Func<string, string> PackageOnDiskForTest;

        // RimWorld lists only the first copy of a packageId and drops the rest, so a Workshop item it dropped is read from disk.
        internal static string PackageIdOnDisk(string workshopId)
        {
            if (PackageOnDiskForTest != null) return PackageOnDiskForTest(workshopId);
            if (!Mods.KmhWorkshop.IsWorkshopId(workshopId) || _workshopFolder == null) return null;
            lock (_packageOnDisk)
                if (_packageOnDisk.TryGetValue(workshopId, out string known)) return known;
            string found = null;
            try
            {
                string about = System.IO.Path.Combine(_workshopFolder, workshopId, "About", "About.xml");
                if (!System.IO.File.Exists(about)) return null;
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                using (var reader = System.Xml.XmlReader.Create(about, settings))
                    found = System.Xml.Linq.XDocument.Load(reader).Root?.Element("packageId")?.Value?.Trim();
            }
            catch (Exception ex) { KmhLog.Debug($"Mods: reading Workshop {workshopId}'s About.xml failed: {ex.Message}"); }
            // A miss is not kept: the item may simply not have finished downloading.
            if (!string.IsNullOrEmpty(found)) lock (_packageOnDisk) _packageOnDisk[workshopId] = found;
            return found;
        }

        // Snapshot for worker-thread comparisons; RimWorld replaces its list only on a main-thread rescan.
        public static List<Mods.InstalledMod> InstalledCached()
        {
            List<Mods.InstalledMod> snap = _installedSnapshot;
            if (snap != null) return snap;
            return _installedSnapshot = Installed();
        }

        // Call after ModLister.RebuildModList on the main thread.
        public static void Rescanned() => _installedSnapshot = Installed();

        // The CURRENT load order, which is not the order installed mods are enumerated in.
        public static List<string> ActiveOrder()
        {
            List<string> ids = new List<string>();
            try
            {
                foreach (string id in ModsConfig.ActiveModsInLoadOrder == null
                                          ? new List<string>()
                                          : IdsOf(ModsConfig.ActiveModsInLoadOrder))
                    ids.Add(id);
            }
            catch (Exception ex) { KmhLog.Warn($"Mods: reading the load order failed: {ex.Message}"); }
            return ids;
        }

        private static IEnumerable<string> IdsOf(IEnumerable<ModMetaData> mods)
        {
            foreach (ModMetaData m in mods)
                if (m != null && !string.IsNullOrEmpty(m.PackageId)) yield return m.PackageId;
        }

        internal static void SeedForTest(List<string> names) => _cached = names;

        internal static void SeedAuthorsForTest(Dictionary<string, string> authors) => _authors = authors;

        internal static void SeedIdsForTest(List<string> ids) => _cachedIds = ids;

        internal static void SeedInstalledForTest(List<Mods.InstalledMod> installed) => _installedSnapshot = installed;
    }
}
