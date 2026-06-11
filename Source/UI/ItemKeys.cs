using RimWorld;
using Verse;

namespace KMHPatch.UI
{
    // Composed item keys carry material + quality through every item flow (treasury, marketplace, quests):
    //   "Steel"                          plain item, no stuff/quality
    //   "MeleeWeapon_LongSword|Plasteel|5"  def | stuff defName (may be empty) | quality index
    // Quality index: 0 = none/any, 1..7 = Awful..Legendary (QualityCategory + 1). Mirrors the server's
    // Util/ItemKey.cs - same format on both sides or vault keys stop matching
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

        // true when an actual quality satisfies a requirement (0 = no requirement; otherwise required-or-better)
        public static bool Meets(int actualIndex, int requiredIndex)
            => requiredIndex <= 0 || actualIndex >= requiredIndex;

        public static string QualityName(int idx) => idx switch
        {
            1 => "Awful", 2 => "Poor", 3 => "Normal", 4 => "Good",
            5 => "Excellent", 6 => "Masterwork", 7 => "Legendary",
            _ => "",
        };

        // 1..7 from a Thing's CompQuality, 0 when the item has no quality
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

        // "Excellent plasteel longsword" from a composed key; plain defs fall through to the normal label
        public static string LabelForKey(string key)
        {
            Split(key, out string def, out string stuff, out int q);
            string baseName = ItemLabels.ResolveStuffedLabel(def, stuff);
            return q > 0 ? $"{QualityName(q)} {baseName}" : baseName;
        }
    }
}
