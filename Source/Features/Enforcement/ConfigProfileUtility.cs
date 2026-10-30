using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace KMHPatch.Features.Enforcement
{
    internal static class ConfigProfileUtility
    {
        public static readonly HashSet<string> DefaultExcludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "KeyPrefs.xml",
            "Prefs.xml",
            "Resolution.xml",
            "UILayout.xml",
            "Knowledge.xml",
            "LastPlayedVersion.txt",
            "ModsConfig.xml",
            "backup_ModsConfig.xml",
            "TrueTerrainColorsCache.xml",
            "KMH_ACTIVE_PROFILE.txt",
            "KMH_ENFORCE_APPLYING",
        };

        public static readonly string[] DefaultExcludePrefixes = { "backup_" };
        public static readonly string[] DefaultExcludeSuffixes =
        {
            "Cache.xml", "Cache.json",
            "_KMHPatchMod.xml",
            "_ModConfigSetter.xml",
        };

        public static readonly HashSet<string> DefaultExcludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ModFeatures", // RimWorld's "seen this mod's new feature" tracking
            "RimHUD",      // personal HUD layout (Config/Docked/Floating)
        };

        public static void ExtractZipBytesToFolder(byte[] zipBytes, string destinationFolder)
        {
            if (zipBytes == null || zipBytes.Length == 0) throw new ArgumentException(nameof(zipBytes));
            if (string.IsNullOrEmpty(destinationFolder)) throw new ArgumentException(nameof(destinationFolder));

            Directory.CreateDirectory(destinationFolder);
            string destRoot = Path.GetFullPath(destinationFolder);
            if (!destRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                destRoot += Path.DirectorySeparatorChar;

            // Read before ZipArchive parses a central directory that could list millions of entries.
            int declared = DeclaredEntryCount(zipBytes);
            if (declared < 0) throw new InvalidDataException("config profile is not a readable zip");
            if (declared > MaxZipEntries)
                throw new InvalidDataException($"config profile declares {declared} entries, over the {MaxZipEntries} limit");

            int  entries = 0;
            long written = 0;
            using (var ms = new MemoryStream(zipBytes))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                // Checked before anything is written: on Windows the second twin silently overwrites the first.
                var names = new List<string>();
                foreach (ZipArchiveEntry entry in zip.Entries)
                    if (Unpackable(entry.FullName)) names.Add(entry.FullName);
                RefuseCaseTwins(names, "config profile");

                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (++entries > MaxZipEntries)
                        throw new InvalidDataException($"config profile holds more than {MaxZipEntries} entries");
                    // Only a flat settings name is unpacked: folders, programs and shortcuts stay inside the zip.
                    if (!Unpackable(entry.FullName)) continue;
                    string full = Path.GetFullPath(Path.Combine(destinationFolder, entry.FullName));
                    if (!full.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip
                    // Cheap reject on the declared size; the header can lie, so the write below is what really counts.
                    if (entry.Length > MaxZipEntryBytes)
                        throw new InvalidDataException($"'{entry.FullName}' declares {entry.Length} bytes, over the {MaxZipEntryBytes} limit");

                    string outDir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                    written += ExtractBounded(entry, full, MaxZipTotalBytes - written);
                }
            }
        }

        internal static bool Unpackable(string fullName)
            => !string.IsNullOrEmpty(fullName) && fullName.IndexOf('/') < 0 && fullName.IndexOf('\\') < 0
            && ConfigManifest.IsManagedName(fullName) && !ConfigManifest.LooksLikeShortName(fullName) && !ShouldExcludeByName(fullName);

        // The end-of-central-directory record's entry count; -1 when there is none. Zip64 is never needed for settings.
        internal static int DeclaredEntryCount(byte[] zip)
        {
            if (zip == null || zip.Length < 22) return -1;
            int stop = Math.Max(0, zip.Length - 22 - 65535);
            for (int i = zip.Length - 22; i >= stop; i--)
                if (zip[i] == 0x50 && zip[i + 1] == 0x4b && zip[i + 2] == 0x05 && zip[i + 3] == 0x06)
                {
                    int total = zip[i + 10] | (zip[i + 11] << 8);
                    return total == 0xFFFF ? int.MaxValue : total;
                }
            return -1;
        }

        // Case twins are two files on disk but one record in the case-blind journal, so one would never roll back.
        internal static void RefuseCaseTwins(IEnumerable<string> names, string what)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in names)
                if (!seen.Add((n ?? "").Replace('\\', '/')))
                    throw new InvalidDataException($"{what} names '{n}' twice, letter case aside");
        }

        // A community server's profile is untrusted input, and ExtractToFile writes whatever decompresses out of it.
        internal const int  MaxZipEntries    = 10_000;
        internal const long MaxZipEntryBytes = 16L * 1024 * 1024;
        internal const long MaxZipTotalBytes = 128L * 1024 * 1024;

        private static long ExtractBounded(ZipArchiveEntry entry, string destination, long remainingTotal)
        {
            long cap = Math.Min(MaxZipEntryBytes, Math.Max(0, remainingTotal));
            long got = 0;
            byte[] buffer = new byte[64 * 1024];
            using (Stream src = entry.Open())
            using (var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    got += read;
                    if (got > cap)
                    {
                        // Abandoned mid-write on purpose: the caller deletes the staging folder on failure.
                        throw new InvalidDataException(
                            $"'{entry.FullName}' expands past the {cap} byte limit - refusing the profile");
                    }
                    dst.Write(buffer, 0, read);
                }
            }
            return got;
        }

        public static string Sha256Hex(byte[] bytes)
            => bytes == null ? string.Empty : ConfigManifest.HashBytes(bytes);

        public static List<byte[]> SplitIntoChunks(byte[] bytes, int chunkSizeBytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (chunkSizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes));

            var chunks = new List<byte[]>();
            int offset = 0;
            while (offset < bytes.Length)
            {
                int take = Math.Min(chunkSizeBytes, bytes.Length - offset);
                var chunk = new byte[take];
                Buffer.BlockCopy(bytes, offset, chunk, 0, take);
                chunks.Add(chunk);
                offset += take;
            }
            if (chunks.Count == 0) chunks.Add(Array.Empty<byte>());
            return chunks;
        }

        public static bool ShouldExcludeByName(string nameOnly)
        {
            if (string.IsNullOrEmpty(nameOnly)) return true;
            if (DefaultExcludeFiles.Contains(nameOnly)) return true;
            foreach (string p in DefaultExcludePrefixes)
                if (nameOnly.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string s in DefaultExcludeSuffixes)
                if (nameOnly.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Publish and apply both call this so their sets stay identical.
        public static bool ShouldExcludeByRelPath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return true;
            string norm = rel.Replace('\\', '/');
            int slash = norm.IndexOf('/');
            if (slash > 0 && DefaultExcludeDirs.Contains(norm.Substring(0, slash))) return true;
            return ShouldExcludeByName(Path.GetFileName(norm));
        }
    }
}
