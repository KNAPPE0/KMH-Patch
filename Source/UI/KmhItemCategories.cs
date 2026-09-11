using System;
using System.Collections.Generic;
using Verse;

namespace KMHPatch.UI
{
    // First matching bucket wins, so the declaration order below is the precedence.
    public static class KmhItemCategories
    {
        private static readonly (string Label, string[] Cats)[] Buckets =
        {
            ("Meat",                      new[]{ "MeatRaw" }),
            ("Animal Products",           new[]{ "AnimalProductRaw", "EggsUnfertilized", "EggsFertilized" }),
            ("Raw Food / Crops",          new[]{ "PlantFoodRaw", "FoodRaw" }),
            ("Meals / Prepared Food",     new[]{ "FoodMeals", "Foods" }),
            ("Leather / Textiles / Wool", new[]{ "Leathers", "Textiles", "Wools" }),
            ("Stone / Blocks",            new[]{ "StoneBlocks", "Chunks" }),
            ("Medicine / Drugs",          new[]{ "Medicine", "Drugs" }),
            ("Components / Manufactured",  new[]{ "Manufactured" }),
            ("Metals / Minerals",         new[]{ "ResourcesRaw" }),
            ("Seeds / Plants",            new[]{ "Plants", "PlantMatter" }),
            ("Weapons / Apparel",         new[]{ "Weapons", "WeaponsRanged", "WeaponsMelee", "Apparel", "ApparelArmor", "ApparelUtility" }),
            ("Body Parts / Implants",     new[]{ "BodyParts", "BodyPartsArtificial", "BodyPartsNatural" }),
            ("Buildings / Furniture",     new[]{ "Buildings", "BuildingsFurniture", "BuildingsMisc", "BuildingsArt", "BuildingsPower", "BuildingsProduction" }),
        };

        // Dropdown order (All + Misc bracket the buckets). Matches the buckets above plus the catch-all.
        public static readonly string[] Dropdown =
        {
            "All", "Raw Food / Crops", "Meat", "Animal Products", "Meals / Prepared Food",
            "Leather / Textiles / Wool", "Stone / Blocks", "Metals / Minerals", "Medicine / Drugs",
            "Components / Manufactured", "Seeds / Plants", "Weapons / Apparel", "Body Parts / Implants",
            "Buildings / Furniture", "Misc",
        };

        public static string CategoryOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return "Misc";
            ItemKeys.Split(key, out string defName, out _, out _);
            ThingDef td;
            try { td = DefDatabase<ThingDef>.GetNamedSilentFail(defName); } catch { td = null; }
            if (td?.thingCategories == null || td.thingCategories.Count == 0) return "Misc";
            HashSet<string> anc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ThingCategoryDef c in td.thingCategories)
                for (ThingCategoryDef cur = c; cur != null; cur = cur.parent) anc.Add(cur.defName);
            foreach ((string label, string[] cats) in Buckets)
                foreach (string cat in cats)
                    if (anc.Contains(cat)) return label;
            return "Misc";
        }

        public static bool Matches(string key, string category)
            => string.IsNullOrEmpty(category) || string.Equals(category, "All", StringComparison.OrdinalIgnoreCase)
               || string.Equals(CategoryOf(key), category, StringComparison.OrdinalIgnoreCase);
    }
}
