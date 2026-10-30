using System;
using System.Collections.Generic;

namespace KMHPatch.Features.Global
{
    internal static class BrowserTags
    {
        // Declaration order is the display order: play style first, then how the server is run.
        public static readonly string[] Known =
        {
            "vanilla", "modded", "questing", "building", "economy", "events",
            "pve", "pvp", "casual", "hardcore", "roleplay",
            "beginner-friendly", "whitelist", "18+",
        };

        private static readonly Dictionary<string, string> Labels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "pve", "PvE" }, { "pvp", "PvP" },
                { "beginner-friendly", "Beginner friendly" },
                { "18+", "18+" },
            };

        public static string Canonical(string tag) => (tag ?? "").Trim().ToLowerInvariant();

        public static bool IsKnown(string tag)
        {
            string c = Canonical(tag);
            foreach (string k in Known) if (k == c) return true;
            return false;
        }

        public static string Label(string tag)
        {
            string c = Canonical(tag);
            if (c.Length == 0) return "";
            if (Labels.TryGetValue(c, out string known)) return known;
            return char.ToUpperInvariant(c[0]) + c.Substring(1).Replace('-', ' ');
        }

        public static List<string> InUse(IEnumerable<ServerCardDto> cards, Dictionary<string, int> counts)
        {
            counts.Clear();
            if (cards != null)
                foreach (ServerCardDto c in cards)
                {
                    if (c?.Tags == null) continue;
                    foreach (string raw in c.Tags)
                    {
                        string t = Canonical(raw);
                        if (t.Length == 0) continue;
                        counts.TryGetValue(t, out int n);
                        counts[t] = n + 1;
                    }
                }

            var ordered = new List<string>();
            foreach (string k in Known) if (counts.ContainsKey(k)) ordered.Add(k);

            var custom = new List<string>();
            foreach (KeyValuePair<string, int> kv in counts) if (!IsKnown(kv.Key)) custom.Add(kv.Key);
            custom.Sort(StringComparer.OrdinalIgnoreCase);

            ordered.AddRange(custom);
            return ordered;
        }
    }
}
