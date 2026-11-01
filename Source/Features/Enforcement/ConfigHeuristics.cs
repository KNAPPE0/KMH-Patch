using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        private static readonly string[] Markers =
        {
            "window", "position", "posx", "posy", "anchor",
            "opacity", "color", "colour", "fontsize", "font", "texture",
            "volume", "sound", "mute", "audio",
            "hash", "cache", "lastloaded", "lastseen", "lastversion", "lastplayed",
            "loadingtimes", "startuploading", "tooltip",
        };

        // As often gameplay as display (traderWealthOffset, raidPointsScale, Alpha Animals), so personal only beside a display word.
        private static readonly string[] DisplayOnly = { "alpha", "offset", "scale" };

        private static readonly HashSet<string> DisplayWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "ui", "gui", "hud", "icon", "label", "text", "overlay", "draw", "render", "graphic", "sprite", "button", "bar",
            "screen", "camera", "zoom", "menu", "tab", "panel", "dialog", "widget", "gizmo", "pos", "x", "y",
        };

        private static readonly Regex Word = new Regex(@"\p{Lu}+(?!\p{Ll})|\p{Lu}?\p{Ll}+|\d+", RegexOptions.CultureInvariant);

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
                        if (IsPersonalField(field)) personal++;
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

        private static IEnumerable<string> Fields(string path)
        {
            XElement settings;
            try { settings = XDocument.Parse(File.ReadAllText(path)).Descendants("ModSettings").FirstOrDefault(); }
            catch { yield break; }
            if (settings == null) yield break;
            foreach (XElement el in settings.Elements()) yield return el.Name.LocalName;
        }

        // Public so the preserve-personal merge classifies fields by the exact same rules as the dialog hint.
        // A marker has to start a word, so "position" is not found inside raidCompositionMode.
        public static bool IsPersonalField(string field)
        {
            if (string.IsNullOrEmpty(field)) return false;
            MatchCollection found = Word.Matches(field);
            if (found.Count < 2)
            {
                // No word breaks to go by (windowposx), so searched as a whole.
                string f = field.ToLowerInvariant();
                foreach (string m in Markers) if (f.Contains(m)) return true;
                return false;
            }
            var words = new List<string>();
            foreach (Match w in found) words.Add(w.Value.ToLowerInvariant());
            bool display = words.Exists(DisplayWords.Contains);
            for (int i = 0; i < words.Count; i++)
            {
                string from = string.Concat(words.GetRange(i, words.Count - i));
                foreach (string m in Markers) if (from.StartsWith(m, StringComparison.Ordinal)) return true;
                if (display) foreach (string m in DisplayOnly) if (from.StartsWith(m, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
