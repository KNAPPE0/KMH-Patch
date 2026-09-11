using System;
using RimWorld;
using Verse;

namespace KMHPatch.UI
{
    // Item keys preserve stuff + quality across treasury, market, and quest flows; format must match server ItemKey.cs or vault lookups break.
    public static class ItemKeys
    {
        public const char Sep = '|';

        public static string Compose(string defName, string stuffDefName, int qualityIndex)
        {
            if (string.IsNullOrEmpty(stuffDefName) && qualityIndex <= 0) return defName ?? "";
            return $"{defName}{Sep}{stuffDefName ?? ""}{Sep}{qualityIndex}";
        }

        public static void Split(string key, out string defName, out string stuffDefName, out int qualityIndex)
        {
            defName = key ?? ""; stuffDefName = ""; qualityIndex = 0;
            if (string.IsNullOrEmpty(key) || key.IndexOf(Sep) < 0) return;
            string[] parts = key.Split(Sep);
            defName = parts[0];
            if (parts.Length > 1) stuffDefName = parts[1];
            if (parts.Length > 2 && int.TryParse(parts[2], out int q) && q >= 0 && q <= 7) qualityIndex = q;
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
            Split(key, out string def, out string stuff, out int q);
            string baseName = ItemLabels.ResolveStuffedLabel(def, stuff);
            return q > 0 ? $"{QualityName(q)} {baseName}" : baseName;
        }
    }
}
