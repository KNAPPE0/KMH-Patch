using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.UI
{
    // Read-only helper for getting "what items are in the caravan the player has currently selected" - used by
    // Treasury Deposit and (eventually) Marketplace Post to populate their item pickers
    //
    // Vanilla-API only. We don't depend on any RWT helper so
    // this stays clean-room
    internal static class CaravanReader
    {
        // The caravan whose context is "active" right now. RWT-style code typically reads this from
        // SessionHandler.ChosenCaravan; vanilla has Find.WorldSelector.SelectedCaravans. We prefer the vanilla
        // selection so the helper works even when RWT session state hasn't tracked the latest selection yet
        //
        // Returns null when no caravan is selected. Callers should surface a notification rather than silently
        // no-op
        public static Caravan GetSelectedCaravan()
        {
            try
            {
                // RimWorld 1.6: WorldSelector exposes SelectedObjects (mixed WorldObject types). Filter down to the
                // first selected Caravan if any
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

        // Defname -> stack-summed count across every container in the caravan (pawn inventories + carriers).
        // Filters down to "real" items by skipping pawns themselves and anything without a stackable def
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
                // Defensive - RimWorld's inventory iterators throw rarely on weird containers; we'd rather show an
                // empty picker than crash the dialog open
            }
            return result;
        }
    }
}
