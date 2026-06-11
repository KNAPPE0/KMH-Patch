using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.UI
{
    // The verify-and-transfer helper for every "move goods between the colony and a KMH ledger" flow (treasury
    // deposit/withdraw, marketplace post/buy, site rewards...). Source/destination is the player's selected caravan
    // - the same caravan the pickers already read from
    //
    // Deposits MUST verify the caravan actually holds the amount and remove it before telling the server, so a
    // player can't deposit/list/sell items they don't have. Grants from the server (withdraw, purchase) materialize
    // back into the caravan. Vanilla API only - stays clean-room
    internal static class ColonyGoods
    {
        public static ThingDef Silver => ThingDefOf.Silver;

        public static ThingDef Def(string defName)
            => string.IsNullOrWhiteSpace(defName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(defName);

        // The caravan goods flow through. Returns null + a player-facing reason when nothing is selected
        public static Caravan RequireCaravan(out string error)
        {
            Caravan c = CaravanReader.GetSelectedCaravan();
            if (c == null)
            {
                error = "Select a caravan first - that's where the items come from (and go back to).";
                return null;
            }
            error = null;
            return c;
        }

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

        // Deliver to the player without needing a caravan: into the selected caravan if there is one, otherwise a
        // drop pod onto the home map. Use
        // for withdrawals / purchases / site rewards so they never get stuck for
        // lack of a caravan.
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
