using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

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

        // Top-level Config subfolders that are per-player, never enforced.
        public static readonly HashSet<string> DefaultExcludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ModFeatures", // RimWorld's "seen this mod's new feature" tracking
            "RimHUD",      // personal HUD layout (Config/Docked/Floating)
        };

        private const long MaxFileBytes = 25_000_000; // skip giant files (caches/log dumps)

        // Zips the folder minus the denylist and anything extraExclude rejects; subfolder paths are preserved.
        public static byte[] CreateConfigZipBytes(string configFolderPath, Func<string, bool> extraExclude = null)
        {
            if (string.IsNullOrEmpty(configFolderPath)) throw new ArgumentException(nameof(configFolderPath));
            if (!Directory.Exists(configFolderPath)) throw new DirectoryNotFoundException(configFolderPath);

            string root = NormalizeDir(configFolderPath);
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    {
                        string entryName = MakeRelative(root, file).Replace("\\", "/");
                        if (entryName.Length == 0) continue;
                        if (ShouldExcludeByRelPath(entryName)) continue;
                        if (extraExclude != null && extraExclude(Path.GetFileName(file))) continue;

                        try
                        {
                            if (new FileInfo(file).Length > MaxFileBytes) continue;
                            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                        }
                        catch { /* skip a file we can't read; the rest still ship */ }
                    }
                }
                return ms.ToArray();
            }
        }

        // Guards zip-slip: entries that try to escape the destination via .. or an absolute path.
        public static void ExtractZipBytesToFolder(byte[] zipBytes, string destinationFolder)
        {
            if (zipBytes == null || zipBytes.Length == 0) throw new ArgumentException(nameof(zipBytes));
            if (string.IsNullOrEmpty(destinationFolder)) throw new ArgumentException(nameof(destinationFolder));

            Directory.CreateDirectory(destinationFolder);
            string destRoot = Path.GetFullPath(destinationFolder);
            if (!destRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                destRoot += Path.DirectorySeparatorChar;

            using (var ms = new MemoryStream(zipBytes))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string full = Path.GetFullPath(Path.Combine(destinationFolder, entryPath));
                    if (!full.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip

                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(full); continue; } // dir entry

                    string outDir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                    entry.ExtractToFile(full, overwrite: true);
                }
            }
        }

        public static string Sha256Hex(byte[] bytes)
        {
            if (bytes == null) return string.Empty;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

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

        // Folder-aware exclude plus the filename rules; publish and apply both call this so their sets stay identical.
        public static bool ShouldExcludeByRelPath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return true;
            string norm = rel.Replace('\\', '/');
            int slash = norm.IndexOf('/');
            if (slash > 0 && DefaultExcludeDirs.Contains(norm.Substring(0, slash))) return true;
            return ShouldExcludeByName(Path.GetFileName(norm));
        }

        private static string NormalizeDir(string path)
        {
            string p = Path.GetFullPath(path);
            return p.EndsWith(Path.DirectorySeparatorChar.ToString()) ? p.Substring(0, p.Length - 1) : p;
        }

        private static string MakeRelative(string rootDir, string fullPath)
        {
            string root = NormalizeDir(rootDir) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(fullPath);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : Path.GetFileName(fullPath);
        }
    }
}
