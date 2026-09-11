using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.UI
{
    // Vanilla API only, depending on no RWT helper, so this stays clean-room.
    internal static class CaravanReader
    {
        // Vanilla's selector rather than RWT session state, which can lag the player's latest selection.
        public static Caravan GetSelectedCaravan()
        {
            try
            {
                List<WorldObject> sel = Find.WorldSelector?.SelectedObjects;
                if (sel != null)
                {
                    for (int i = 0; i < sel.Count; i++)
                    {
                        if (sel[i] is Caravan c) return c;
                    }
                }
            }
            catch { /* fall through */ }
            return null;
        }

        public static Dictionary<string, int> ReadInventory(Caravan caravan)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            if (caravan == null) return result;

            try
            {
                IEnumerable<Thing> items = CaravanInventoryUtility.AllInventoryItems(caravan);
                foreach (Thing t in items)
                {
                    if (t?.def == null) continue;
                    // composed key so different material/quality stacks never merge
                    string key = ItemKeys.Compose(t.def.defName, t.Stuff?.defName, ItemKeys.QualityIndexOf(t));
                    if (!result.TryGetValue(key, out int cur)) cur = 0;
                    result[key] = cur + t.stackCount;
                }
            }
            catch
            {
                // An empty picker beats a dialog that throws on open from an odd container.
            }
            return result;
        }
    }
}
