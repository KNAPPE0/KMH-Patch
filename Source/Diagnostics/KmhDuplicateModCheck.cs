using System;
using System.Collections.Generic;
using Verse;

namespace KMHPatch.Diagnostics
{
    // RimWorld silently loads only the first of several copies sharing a packageId, so an update may not be what runs.
    internal static class KmhDuplicateModCheck
    {
        public const string PackageId = "knappe.kmh.patch";

        // A parked RWT copy left in Mods beside the live one hits the same first-copy-wins trap.
        public const string RwtPackageId = "nova.rimworldtogether";

        internal sealed class Copy
        {
            public string Path = "";
            public bool Active;
        }

        public static void Report()
        {
            try
            {
                List<Copy> own = Find(PackageId);
                Copy legacy = LegacyCopy(GenFilePaths.ModsFolderPath, RunningRoot(PackageId));
                if (legacy != null && !own.Exists(c => Same(c.Path, legacy.Path))) own.Add(legacy);
                string warning = Describe(own, Constants.BrandName, PackageId);
                if (warning.Length > 0) KmhLog.Warn(warning);
                // On screen as well: renaming the folder leaves the old one beside the new for everyone who unzips.
                string notice = Notice(own);
                if (notice.Length > 0) _pending = notice;
                string rwt = Describe(Find(RwtPackageId), "RimWorld Together", RwtPackageId);
                if (rwt.Length > 0) KmhLog.Warn(rwt);
            }
            catch (Exception ex) { KmhLog.Debug($"Duplicate-mod check failed: {ex.GetType().Name}"); }
        }

        // RimWorld has already parsed every packageId, so this is a list walk, not an About.xml read per mod.
        internal static List<Copy> Find(string packageId)
        {
            List<Copy> found = new List<Copy>();
            string running = RunningRoot(packageId);

            foreach (ModMetaData m in ModLister.AllInstalledMods)
            {
                if (m == null || !string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase)) continue;
                string dir = m.RootDir?.FullName ?? "";
                found.Add(new Copy { Path = dir, Active = Same(dir, running) });
            }
            return found;
        }

        private static string _pending;

        // From the main menu's draw: a window added while the game is still starting is gone before the menu appears.
        internal static void ShowPending()
        {
            string notice = _pending;
            if (string.IsNullOrEmpty(notice) || Verse.Find.WindowStack == null) return;
            _pending = null;
            Verse.Find.WindowStack.Add(new Dialog_MessageBox(notice,
                title: UI.KmhText.Get("KMH_DuplicateMod_Title", "Colony Nexus is installed more than once")));
        }

        // RimWorld 1.7 drops a second local copy from its mod list ("Ignoring the duplicates"), so look on disk.
        internal static Copy LegacyCopy(string modsFolder, string runningRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(modsFolder)) return null;
                string dir = System.IO.Path.Combine(modsFolder, "KMHPatch");
                string about = System.IO.Path.Combine(dir, "About", "About.xml");
                if (!System.IO.File.Exists(about) || Same(dir, runningRoot)) return null;
                if (System.IO.File.ReadAllText(about).IndexOf(PackageId, StringComparison.OrdinalIgnoreCase) < 0) return null;
                return new Copy { Path = dir, Active = false };
            }
            catch (Exception) { return null; }
        }

        private static string RunningRoot(string packageId)
        {
            try
            {
                foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                    if (m != null && string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                        return m.RootDir ?? "";
            }
            catch (Exception) { }
            return "";
        }

        private static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(
                    System.IO.Path.GetFullPath(a).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                    System.IO.Path.GetFullPath(b).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        // Empty when there is nothing to say: one copy is the normal case and must stay silent.
        internal static string Describe(List<Copy> copies, string label, string packageId)
        {
            if (copies == null || copies.Count <= 1) return "";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"{copies.Count} copies of {label} share packageId '{packageId}'. RimWorld loads one and "
                    + "silently ignores the rest, so an update may not be the copy that runs:");
            foreach (Copy c in copies)
                sb.Append($"\n    {(c.Active ? "[RUNNING] " : "[ignored] ")}{c.Path}");
            sb.Append("\n  Keep exactly one. Backups belong outside the Mods folder, and a Steam Workshop copy "
                    + "should be unsubscribed rather than deleted.");
            return sb.ToString();
        }

        internal static string Notice(List<Copy> copies)
        {
            if (copies == null || copies.Count <= 1) return "";
            System.Text.StringBuilder list = new System.Text.StringBuilder();
            foreach (Copy c in copies)
                list.Append("\n").Append(c.Active
                    ? UI.KmhText.Get("KMH_DuplicateMod_Running", "Running: {0}", UI.KmhDisplayText.Inert(c.Path))
                    : UI.KmhText.Get("KMH_DuplicateMod_Ignored", "Ignored: {0}", UI.KmhDisplayText.Inert(c.Path)));
            return UI.KmhText.Get("KMH_DuplicateMod_Body", "RimWorld found more than one copy of Colony Nexus and runs only one of them:")
                 + list + "\n\n"
                 + UI.KmhText.Get("KMH_DuplicateMod_Fix", "Close RimWorld, delete the ignored copies and start it again. An old KMHPatch folder can go entirely: the mod now lives in the ColonyNexus folder, and your settings and account moved with it. A Steam Workshop copy is removed by unsubscribing.");
        }
    }
}
