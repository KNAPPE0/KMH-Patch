using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KMHPatch.Features.Enforcement
{
    internal sealed class ConfigManifestEntry
    {
        public string Path = "";   // file name only; the managed set is flat
        public long   Size;
        public string Hash = "";
    }

    // The single answer to which files are managed; publish, apply, drift, backup and restore must all agree.
    internal sealed class ConfigManifest
    {
        public readonly List<ConfigManifestEntry> Entries = new List<ConfigManifestEntry>();
        public string Hash = "";
        public long TotalBytes;

        public int Count => Entries.Count;

        // RimWorld writes mod settings to the top level of Config; subfolders belong to other tools, so the set is flat.
        internal static readonly HashSet<string> ManagedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xml", ".json" };

        // Never enforced. Docked/Floating are RimHUD layout files it also writes at the Config root.
        internal static readonly HashSet<string> PersonalFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "KeyPrefs.xml", "Prefs.xml", "Resolution.xml", "UILayout.xml", "Knowledge.xml",
                "LastPlayedVersion.txt", "ModsConfig.xml", "backup_ModsConfig.xml",
                "Docked.xml", "Floating.xml",
                "KMH_ACTIVE_PROFILE.txt", "KMH_ENFORCE_APPLYING",
            };

        internal static readonly string[] PersonalPrefixes = { "backup_", "KMH_" };
        internal static readonly string[] PersonalSuffixes =
        {
            "Cache.xml", "Cache.json", "_KMHPatchMod.xml", "_ModConfigSetter.xml",
        };

        // Per file and well past anything real; the transfer caps are what bound a publish.
        internal const long MaxFileBytes = 8L * 1024 * 1024;
        internal const int  MaxFiles     = 4_000;

        // ModsConfig.xml is ModSync's, never Config Enforcement's.
        public static bool IsManagedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            // Flat by construction: a separator, a drive or a traversal segment cannot be a managed name.
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0) return false;
            if (name.IndexOf(':') >= 0 || name == "." || name == "..") return false;
            // Windows' list on every host, so a Linux server refuses what no Windows player could write.
            if (name.IndexOfAny(InvalidNameChars) >= 0 || IsDeviceName(name)) return false;

            if (!ManagedExtensions.Contains(Path.GetExtension(name))) return false;
            if (PersonalFiles.Contains(name)) return false;
            foreach (string p in PersonalPrefixes)
                if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return false;
            foreach (string s in PersonalSuffixes)
                if (name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static readonly char[] InvalidNameChars = BuildInvalidNameChars();

        private static char[] BuildInvalidNameChars()
        {
            var list = new List<char> { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            for (char c = '\0'; c < ' '; c++) list.Add(c);
            return list.ToArray();
        }

        // "COM1.xml" or "NUL.json" opens a device on Windows, in any folder.
        internal static bool IsDeviceName(string name)
        {
            string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL") return true;
            return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && ((stem[3] >= '1' && stem[3] <= '9') || stem[3] == '\u00b9' || stem[3] == '\u00b2' || stem[3] == '\u00b3');
        }

        // Windows opens an 8.3 alias (MODSCO~1.XML) as its long-named file, which may be an excluded one.
        internal static bool LooksLikeShortName(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return false;
            for (int i = 0; i + 1 < segment.Length; i++)
                if (segment[i] == '~' && char.IsDigit(segment[i + 1])) return true;
            return false;
        }

        // Owner-editable mods are left out entirely, not shipped and skipped at apply where a skip could be forgotten.
        public static ConfigManifest Build(string configRoot, Func<string, bool> isSafe = null)
        {
            ConfigManifest m = new ConfigManifest();
            if (string.IsNullOrEmpty(configRoot) || !Directory.Exists(configRoot)) return m;

            List<string> names = new List<string>();
            foreach (string file in Directory.GetFiles(configRoot, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (!IsManagedName(name)) continue;
                if (isSafe != null && isSafe(name)) continue;
                names.Add(name);
            }
            names.Sort(StringComparer.Ordinal);

            foreach (string name in names)
            {
                if (m.Entries.Count >= MaxFiles) break;
                string full = Path.Combine(configRoot, name);
                try
                {
                    var info = new FileInfo(full);
                    if (info.Length > MaxFileBytes) continue;
                    m.Entries.Add(new ConfigManifestEntry { Path = name, Size = info.Length, Hash = HashFile(full) });
                    m.TotalBytes += info.Length;
                }
                catch { /* an unreadable file is not part of the set; the rest still publish */ }
            }

            m.Hash = HashOf(m.Entries);
            return m;
        }

        // Equal hashes mean the same files with the same bytes, which makes republishing an unchanged profile a no-op.
        public static string HashOf(List<ConfigManifestEntry> entries)
        {
            StringBuilder sb = new StringBuilder("kmh.configmanifest.v1\n");
            foreach (ConfigManifestEntry e in entries)
                sb.Append(e.Path).Append('\n').Append(e.Size).Append('\n').Append(e.Hash).Append('\n');
            using (SHA256 sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return Hex(sha.ComputeHash(fs));
        }

        public static string HashBytes(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return Hex(sha.ComputeHash(bytes ?? Array.Empty<byte>()));
        }

        private static string Hex(byte[] h)
        {
            StringBuilder hex = new StringBuilder(h.Length * 2);
            foreach (byte b in h) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }
    }
}
