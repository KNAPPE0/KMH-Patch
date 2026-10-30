using System;
using RimWorld;
using Verse;

namespace KMHPatch.UI
{
    // The format must match the server's ItemKey.cs, or vault lookups break.
    public static class ItemKeys
    {
        public const char Sep = '|';

        public static string Compose(string defName, string stuffDefName, int qualityIndex)
            => Compose(defName, stuffDefName, qualityIndex, null);

        // Every minifiable building in Core shares one minifiedDef, so without innerDefName a bed and a
        // vanometric cell are the same key. Appended fourth, so the server still reads segment 0.
        public static string Compose(string defName, string stuffDefName, int qualityIndex, string innerDefName)
        {
            if (string.IsNullOrEmpty(innerDefName))
            {
                if (string.IsNullOrEmpty(stuffDefName) && qualityIndex <= 0) return defName ?? "";
                return $"{defName}{Sep}{stuffDefName ?? ""}{Sep}{qualityIndex}";
            }
            return $"{defName}{Sep}{stuffDefName ?? ""}{Sep}{qualityIndex}{Sep}{innerDefName}";
        }

        public static void Split(string key, out string defName, out string stuffDefName, out int qualityIndex)
            => Split(key, out defName, out stuffDefName, out qualityIndex, out _);

        public static void Split(string key, out string defName, out string stuffDefName, out int qualityIndex,
                                 out string innerDefName)
        {
            defName = key ?? ""; stuffDefName = ""; qualityIndex = 0; innerDefName = "";
            if (string.IsNullOrEmpty(key) || key.IndexOf(Sep) < 0) return;
            string[] parts = key.Split(Sep);
            defName = parts[0];
            if (parts.Length > 1) stuffDefName = parts[1];
            if (parts.Length > 2 && int.TryParse(parts[2], out int q) && q >= 0 && q <= 7) qualityIndex = q;
            if (parts.Length > 3) innerDefName = parts[3];
        }

        public static string DisplayDefOf(string key)
        {
            Split(key, out string def, out _, out _, out string inner);
            return string.IsNullOrEmpty(inner) ? def : inner;
        }

        public static string KeyFor(Thing t)
        {
            if (t?.def == null) return "";
            string inner = (t as MinifiedThing)?.InnerThing?.def?.defName;
            return Compose(t.def.defName, t.Stuff?.defName, QualityIndexOf(t), inner);
        }

        // Quality 0 means no requirement, not "Awful".
        public static bool Meets(int actualIndex, int requiredIndex)
            => requiredIndex <= 0 || actualIndex >= requiredIndex;

        // Withdrawals and their self-test both route through this, so the rule cannot drift between them.
        public static bool Matches(string key, string targetDefName, string requiredStuff, int requiredQualityIndex)
        {
            Split(key, out string def, out string stuff, out int q);
            if (!string.Equals(def, targetDefName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!Meets(q, requiredQualityIndex)) return false;
            return string.IsNullOrEmpty(requiredStuff)
                || string.Equals(stuff ?? "", requiredStuff, StringComparison.OrdinalIgnoreCase);
        }

        public static string QualityName(int idx) => idx switch
        {
            1 => "Awful", 2 => "Poor", 3 => "Normal", 4 => "Good",
            5 => "Excellent", 6 => "Masterwork", 7 => "Legendary",
            _ => "",
        };

        // 0 when the item has no quality at all, which callers must not read as "Awful".
        public static int QualityIndexOf(Thing t)
        {
            CompQuality cq = t?.TryGetComp<CompQuality>();
            return cq == null ? 0 : (int)cq.Quality + 1;
        }

        public static void ApplyQuality(Thing t, int qualityIndex)
        {
            if (t == null || qualityIndex < 1 || qualityIndex > 7) return;
            t.TryGetComp<CompQuality>()?.SetQuality((QualityCategory)(qualityIndex - 1), ArtGenerationContext.Outsider);
        }

        public static string LabelForKey(string key)
        {
            Split(key, out string def, out string stuff, out int q, out string inner);
            // A crate labelled "minified thing" tells the player nothing; the building inside is the item.
            string baseName = string.IsNullOrEmpty(inner)
                ? ItemLabels.ResolveStuffedLabel(def, stuff)
                : ItemLabels.ResolveStuffedLabel(inner, stuff) + " (minified)";
            return q > 0 ? $"{QualityName(q)} {baseName}" : baseName;
        }
    }
}
