using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Hint only: scans Config/Mod_*.xml field names for per-player patterns; suggests, never enforces.
    internal static class ConfigHeuristics
    {
        public enum Verdict { NoConfig, Empty, Gameplay, Mixed, Personal }

        public readonly struct Hint
        {
            public readonly Verdict Verdict;
            public readonly int Personal;
            public readonly int Total;
            public Hint(Verdict v, int p, int t) { Verdict = v; Personal = p; Total = t; }
        }

        // Lowercased substrings that mark a setting field as player-specific.
        private static readonly string[] Markers =
        {
            "window", "position", "posx", "posy", "anchor", "offset",
            "scale", "opacity", "alpha", "color", "colour", "fontsize", "font", "texture",
            "volume", "sound", "mute", "audio",
            "hash", "cache", "lastloaded", "lastseen", "lastversion", "lastplayed",
            "loadingtimes", "startuploading", "tooltip",
        };

        private static readonly Dictionary<string, Hint> _cache = new Dictionary<string, Hint>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache() { lock (_cache) _cache.Clear(); }

        public static Hint Classify(ModContentPack mod)
        {
            if (mod == null) return new Hint(Verdict.NoConfig, 0, 0);
            string key = mod.PackageId ?? mod.Name ?? "";
            lock (_cache) { if (_cache.TryGetValue(key, out Hint cached)) return cached; }
            Hint hint = Compute(mod);
            lock (_cache) _cache[key] = hint;
            return hint;
        }

        private static Hint Compute(ModContentPack mod)
        {
            try
            {
                if (string.IsNullOrEmpty(mod.FolderName)) return new Hint(Verdict.NoConfig, 0, 0);
                string dir = GenFilePaths.ConfigFolderPath;
                string prefix = "Mod_" + mod.FolderName + "_";

                int personal = 0, total = 0; bool any = false;
                foreach (string path in Directory.GetFiles(dir, prefix + "*.xml", SearchOption.TopDirectoryOnly))
                {
                    any = true;
                    foreach (string field in Fields(path))
                    {
                        total++;
                        if (IsPersonal(field)) personal++;
                    }
                }

                if (!any) return new Hint(Verdict.NoConfig, 0, 0);
                if (total == 0) return new Hint(Verdict.Empty, 0, 0);
                if (personal == total) return new Hint(Verdict.Personal, personal, total);
                if (personal > 0) return new Hint(Verdict.Mixed, personal, total);
                return new Hint(Verdict.Gameplay, 0, total);
            }
            catch (Exception ex) { KmhLog.Warn($"Heuristics: classify '{mod?.Name}' failed: {ex.Message}"); return new Hint(Verdict.NoConfig, 0, 0); }
        }

        // The direct setting-field names under <ModSettings> (skips list <li> values).
        private static IEnumerable<string> Fields(string path)
        {
            XElement settings;
            try { settings = XDocument.Parse(File.ReadAllText(path)).Descendants("ModSettings").FirstOrDefault(); }
            catch { yield break; }
            if (settings == null) yield break;
            foreach (XElement el in settings.Elements()) yield return el.Name.LocalName;
        }

        // Public so the preserve-personal merge classifies fields by the exact same rules as the dialog hint.
        public static bool IsPersonalField(string field)
        {
            if (string.IsNullOrEmpty(field)) return false;
            string f = field.ToLowerInvariant();
            foreach (string m in Markers) if (f.Contains(m)) return true;
            return false;
        }

        private static bool IsPersonal(string field) => IsPersonalField(field);
    }
}
