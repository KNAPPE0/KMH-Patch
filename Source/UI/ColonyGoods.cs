using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.UI
{
    // Verifies and moves goods for KMH ledger flows, removing deposits before server sends and materializing grants back.
    internal static class ColonyGoods
    {
        public static ThingDef Silver => ThingDefOf.Silver;

        public static ThingDef Def(string defName)
            => string.IsNullOrWhiteSpace(defName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(defName);

        // -- verify --

        public static int Count(Caravan caravan, ThingDef def)
        {
            if (caravan == null || def == null) return 0;
            int total = 0;
            try
            {
                foreach (Thing t in CaravanInventoryUtility.AllInventoryItems(caravan))
                    if (t?.def == def) total += t.stackCount;
            }
            catch { /* weird container - treat as 0 */ }
            return total;
        }

        public static int CountSilver(Caravan caravan) => Count(caravan, Silver);

        public static bool Has(Caravan caravan, ThingDef def, int qty)
            => def != null && qty > 0 && Count(caravan, def) >= qty;

        // -- remove (deposit / list / sell) --

        // Removes exactly qty across stacks; returns false (and removes nothing)
        // if the caravan doesn't hold enough.
        public static bool TryRemove(Caravan caravan, ThingDef def, int qty)
        {
            if (caravan == null || def == null || qty <= 0) return false;

            List<Thing> matches;
            try { matches = CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => t?.def == def).ToList(); }
            catch { return false; }

            int total = matches.Sum(t => t.stackCount);
            if (total < qty) return false; // never partial-remove

            int remaining = qty;
            foreach (Thing t in matches)
            {
                if (remaining <= 0) break;
                int take = Math.Min(t.stackCount, remaining);
                remaining -= take;
                if (take >= t.stackCount) t.Destroy(DestroyMode.Vanish);
                else                      t.stackCount -= take;
            }
            return remaining <= 0;
        }

        public static bool TryRemoveSilver(Caravan caravan, int amount) => TryRemove(caravan, Silver, amount);

        // -- give (withdraw / purchase / reward) --

        public static void Give(Caravan caravan, ThingDef def, int qty)
        {
            if (caravan == null || def == null || qty <= 0) return;
            try
            {
                int remaining  = qty;
                int stackLimit = Math.Max(1, def.stackLimit);
                while (remaining > 0)
                {
                    int take  = Math.Min(stackLimit, remaining);
                    Thing t   = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                    t.stackCount = take;
                    CaravanInventoryUtility.GiveThing(caravan, t);
                    remaining -= take;
                }
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"ColonyGoods.Give failed for {def.defName} x{qty}: {ex.Message}"); }
        }

        public static void GiveSilver(Caravan caravan, int amount) => Give(caravan, Silver, amount);

        // Deliver to selected caravan or drop pod home, so withdrawals, purchases, and rewards never need a caravan.
        public static void Deliver(ThingDef def, int qty)
        {
            if (def == null || qty <= 0) return;
            Caravan caravan = CaravanReader.GetSelectedCaravan();
            if (caravan != null) { Give(caravan, def, qty); return; }
            DropToHomeMap(def, qty);
        }

        public static void DeliverSilver(int amount) => Deliver(Silver, amount);

        // -- composed-key paths (def|stuff|quality) so material + quality survive every transfer --

        public static int CountKey(Caravan caravan, string key)
        {
            if (caravan == null || string.IsNullOrEmpty(key)) return 0;
            ItemKeys.Split(key, out string defName, out string stuff, out int q);
            int total = 0;
            try
            {
                foreach (Thing t in CaravanInventoryUtility.AllInventoryItems(caravan))
                    if (MatchesKey(t, defName, stuff, q)) total += t.stackCount;
            }
            catch { /* weird container - treat as 0 */ }
            return total;
        }

        // Removes exactly qty of stacks matching the key's def + stuff + quality; all-or-nothing like TryRemove
        public static bool TryRemoveKey(Caravan caravan, string key, int qty)
        {
            if (caravan == null || string.IsNullOrEmpty(key) || qty <= 0) return false;
            ItemKeys.Split(key, out string defName, out string stuff, out int q);

            List<Thing> matches;
            try { matches = CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => MatchesKey(t, defName, stuff, q)).ToList(); }
            catch { return false; }

            int total = matches.Sum(t => t.stackCount);
            if (total < qty) return false;

            int remaining = qty;
            foreach (Thing t in matches)
            {
                if (remaining <= 0) break;
                int take = Math.Min(t.stackCount, remaining);
                remaining -= take;
                if (take >= t.stackCount) t.Destroy(DestroyMode.Vanish);
                else                      t.stackCount -= take;
            }
            return remaining <= 0;
        }

        // Deliver a composed key: spawn with the right material and stamp the quality back on
        public static void DeliverKey(string key, int qty)
        {
            if (string.IsNullOrEmpty(key) || qty <= 0) return;
            ItemKeys.Split(key, out string defName, out string stuffName, out int q);
            ThingDef def = Def(defName);
            if (def == null) { Diagnostics.KmhLog.Warn($"ColonyGoods.DeliverKey: unknown def '{defName}'"); return; }
            ThingDef stuff = string.IsNullOrEmpty(stuffName) ? null : Def(stuffName);

            Caravan caravan = CaravanReader.GetSelectedCaravan();
            if (caravan != null)
            {
                foreach (Thing t in MakeStacks(def, stuff, q, qty))
                    CaravanInventoryUtility.GiveThing(caravan, t);
                return;
            }
            DropToHomeMap(def, stuff, q, qty);
        }

        // No-caravan deposits pull from base stockpiles, verifying and removing only stored, unforbidden items.
        // Use the current map, or any home map if none is active.
        public static Map DepositMap()
        {
            Map cur = Find.CurrentMap;
            if (cur != null && cur.IsPlayerHome) return cur;
            return Find.AnyPlayerHomeMap;
        }

        private static IEnumerable<Thing> StoredOnMap(Map map, ThingDef def)
        {
            if (map?.listerThings == null || def == null) yield break;
            foreach (Thing t in map.listerThings.ThingsOfDef(def))
                if (t != null && t.Spawned && t.IsInValidStorage()) yield return t;
        }

        public static int CountSilverOnMap(Map map) => CountOnMap(map, Silver);

        // Plain-def map count/remove (ignores material + quality), mirroring the caravan Count/TryRemove pair.
        public static int CountOnMap(Map map, ThingDef def)
        {
            int total = 0;
            try { foreach (Thing t in StoredOnMap(map, def)) total += t.stackCount; }
            catch { /* weird storage - treat as 0 */ }
            return total;
        }

        public static bool TryRemoveOnMap(Map map, ThingDef def, int qty)
        {
            if (map == null || def == null || qty <= 0) return false;
            List<Thing> matches;
            try { matches = StoredOnMap(map, def).ToList(); }
            catch { return false; }
            return RemoveExactFromMap(matches, qty);
        }

        public static int CountOnMapKey(Map map, string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            ItemKeys.Split(key, out string defName, out string stuff, out int q);
            ThingDef def = Def(defName);
            if (def == null) return 0;
            int total = 0;
            try { foreach (Thing t in StoredOnMap(map, def)) if (MatchesKey(t, defName, stuff, q)) total += t.stackCount; }
            catch { /* weird storage - treat as 0 */ }
            return total;
        }

        public static bool TryRemoveSilverOnMap(Map map, int amount) => TryRemoveOnMap(map, Silver, amount);

        public static bool TryRemoveOnMapKey(Map map, string key, int qty)
        {
            if (map == null || string.IsNullOrEmpty(key) || qty <= 0) return false;
            ItemKeys.Split(key, out string defName, out string stuff, out int q);
            ThingDef def = Def(defName);
            if (def == null) return false;
            List<Thing> matches;
            try { matches = StoredOnMap(map, def).Where(t => MatchesKey(t, defName, stuff, q)).ToList(); }
            catch { return false; }
            return RemoveExactFromMap(matches, qty);
        }

        // All-or-nothing removal across stacks. SplitOff (rather than a raw stackCount decrement) keeps the map's reservation + UI state consistent for spawned things.
        private static bool RemoveExactFromMap(List<Thing> matches, int qty)
        {
            int total = matches.Sum(t => t.stackCount);
            if (total < qty) return false; // never partial-remove
            int remaining = qty;
            foreach (Thing t in matches)
            {
                if (remaining <= 0) break;
                int take = Math.Min(t.stackCount, remaining);
                remaining -= take;
                if (take >= t.stackCount) t.Destroy(DestroyMode.Vanish);
                else                      t.SplitOff(take).Destroy(DestroyMode.Vanish);
            }
            return remaining <= 0;
        }

        // Composed-key inventory of the colony's stored goods, for the deposit picker when there's no caravan.
        public static Dictionary<string, int> ReadStoredInventory(Map map)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (map?.listerThings == null) return result;
            try
            {
                // HaulableEver is the pre-indexed set of tradeable items - far cheaper than scanning AllThings
                // (which includes filth, plants, buildings) on every picker open.
                foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                {
                    if (t?.def == null || !t.Spawned || !t.IsInValidStorage()) continue;
                    if (t.def.category != ThingCategory.Item) continue;
                    string key = ItemKeys.Compose(t.def.defName, t.Stuff?.defName, ItemKeys.QualityIndexOf(t));
                    result[key] = (result.TryGetValue(key, out int cur) ? cur : 0) + t.stackCount;
                }
            }
            catch { /* rather show an empty picker than crash the dialog */ }
            return result;
        }

        private static bool MatchesKey(Thing t, string defName, string stuffName, int qualityIndex)
        {
            if (t?.def == null || !string.Equals(t.def.defName, defName, StringComparison.Ordinal)) return false;
            string ts = t.Stuff?.defName ?? "";
            if (!string.Equals(ts, stuffName ?? "", StringComparison.Ordinal)) return false;
            return ItemKeys.QualityIndexOf(t) == qualityIndex;
        }

        private static List<Thing> MakeStacks(ThingDef def, int qty) => MakeStacks(def, null, 0, qty);

        private static List<Thing> MakeStacks(ThingDef def, ThingDef stuff, int qualityIndex, int qty)
        {
            List<Thing> things = new List<Thing>();
            int remaining = qty, stackLimit = Math.Max(1, def.stackLimit);
            while (remaining > 0)
            {
                int take = Math.Min(stackLimit, remaining);
                Thing t  = ThingMaker.MakeThing(def, def.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(def)) : null);
                t.stackCount = take;
                ItemKeys.ApplyQuality(t, qualityIndex);
                things.Add(t);
                remaining -= take;
            }
            return things;
        }

        private static void DropToHomeMap(ThingDef def, int qty) => DropToHomeMap(def, null, 0, qty);

        private static void DropToHomeMap(ThingDef def, ThingDef stuff, int qualityIndex, int qty)
        {
            try
            {
                Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
                if (map == null) { Diagnostics.KmhLog.Warn("ColonyGoods.Deliver: no map for a drop pod"); return; }
                IntVec3 cell = DropCellFinder.TradeDropSpot(map);
                DropPodUtility.DropThingsNear(cell, map, MakeStacks(def, stuff, qualityIndex, qty), forbid: false);
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"ColonyGoods drop pod failed for {def.defName} x{qty}: {ex.Message}"); }
        }
    }
}
