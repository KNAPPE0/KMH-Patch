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

        // Item-loss guard: a payload's blob (and the whole txn) must fit the ~64KB router frame, else the message can't
        // send AFTER goods are removed = loss. Checked BEFORE any destruction so an oversized item fails with nothing removed.
        public const int MaxPayloadBytesPerItem        = 40_000;
        public const int MaxPayloadBytesPerTransaction = 48_000;

        // All-or-nothing complex-item capture. Splits the exact qty off the source (still recoverable), captures +
        // validates EVERY piece (non-null, count == qty, within byte limits), and only then destroys them. If ANY
        // capture fails or a payload is oversized, the split-off pieces are handed back (nothing is lost) and it
        // returns false.
        public static bool RemoveKeyCapturing(Caravan caravan, Map map, string key, int qty, out List<Items.KmhThingPayload> captured)
        {
            captured = new List<Items.KmhThingPayload>();
            if (string.IsNullOrEmpty(key) || qty <= 0) return false;
            ItemKeys.Split(key, out string defName, out string stuff, out int q);

            List<Thing> matches;
            try
            {
                matches = caravan != null
                    ? CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => MatchesKey(t, defName, stuff, q)).ToList()
                    : StoredOnMap(map, Def(defName)).Where(t => MatchesKey(t, defName, stuff, q)).ToList();
            }
            catch { return false; }

            if (matches.Sum(t => t.stackCount) < qty) return false;

            // take exactly qty. Whole stacks stay in place (still owned); partial takes are split off
            // (detached) so they can be handed back on abort.
            List<(Thing piece, bool split)> taken = new List<(Thing, bool)>();
            int remaining = qty;
            foreach (Thing t in matches)
            {
                if (remaining <= 0) break;
                int take = Math.Min(t.stackCount, remaining);
                remaining -= take;
                if (take >= t.stackCount) taken.Add((t, false));
                else                      taken.Add((t.SplitOff(take), true));
            }

            // capture + validate all before destroying anything.
            List<Items.KmhThingPayload> caps = new List<Items.KmhThingPayload>();
            long totalBytes = 0; bool ok = true; string failReason = null; int capturedCount = 0;
            foreach ((Thing piece, bool _) in taken)
            {
                Items.KmhThingPayload p = null;
                try { p = Items.KmhThingCapture.Capture(piece); }
                catch (Exception ex) { failReason = ex.Message; }
                if (p == null) { ok = false; failReason = failReason ?? "capture returned null"; break; }
                int bytes = p.ScribeXml?.Length ?? 0;
                if (bytes > MaxPayloadBytesPerItem) { ok = false; failReason = $"payload {bytes}B over per-item limit {MaxPayloadBytesPerItem}B"; break; }
                totalBytes += bytes;
                if (totalBytes > MaxPayloadBytesPerTransaction) { ok = false; failReason = $"payloads total over per-transaction limit {MaxPayloadBytesPerTransaction}B"; break; }
                caps.Add(p); capturedCount += Math.Max(1, p.StackCount);
            }
            if (ok && capturedCount != qty) { ok = false; failReason = $"captured {capturedCount} != requested {qty}"; }

            if (!ok)
            {
                foreach ((Thing piece, bool split) in taken) if (split) DeliverThing(piece);   // give the split pieces back
                Diagnostics.KmhLog.Warn($"KMH capture ABORTED for {defName} x{qty} - {failReason}; items kept (nothing removed).");
                captured = new List<Items.KmhThingPayload>();
                return false;
            }

            // everything captured + validated -> now destroy.
            foreach ((Thing piece, bool _) in taken) piece.Destroy(DestroyMode.Vanish);
            captured = caps;
            return true;
        }

        // Materialize restored payloads (withdraw/grant/rollback). Falls back to a legacy key spawn per payload if a
        // payload can't rebuild, so nothing is silently lost. A single payload may carry more than one stack's worth
        // (e.g. once the server merges same-item deposits into one entry), so it's materialized in stackLimit-sized
        // stacks - one restore per stack, so each keeps the payload's exact state - rather than capping at one stack.
        public static void DeliverPayloads(IEnumerable<Items.KmhThingPayload> payloads)
        {
            if (payloads == null) return;
            foreach (Items.KmhThingPayload p in payloads)
            {
                if (p == null) continue;
                int remaining = Math.Max(1, p.StackCount);
                int guard = 0;
                while (remaining > 0 && guard++ < 100000)
                {
                    Thing t = null;
                    try { t = Items.KmhThingCapture.Restore(p); }
                    catch (Exception ex) { Diagnostics.KmhLog.Warn($"KMH restore threw for {p.DefName}: {ex.Message}"); }
                    if (t == null)
                    {
                        DeliverKey(ItemKeys.Compose(p.DefName, p.StuffDefName, p.Quality), remaining);   // legacy path splits into stacks itself
                        break;
                    }
                    int lim  = t.def != null && t.def.stackLimit > 0 ? t.def.stackLimit : remaining;
                    int give = Math.Min(remaining, lim);
                    t.stackCount = give;
                    DeliverThing(t);
                    remaining -= give;
                }
            }
        }

        // Deliver an already-built Thing to the selected caravan, else a drop pod to the best colony spot.
        public static void DeliverThing(Thing t)
        {
            if (t == null) return;
            try
            {
                Caravan caravan = CaravanReader.GetSelectedCaravan();
                if (caravan != null) { CaravanInventoryUtility.GiveThing(caravan, t); return; }
                Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
                if (map == null) { Diagnostics.KmhLog.Warn("ColonyGoods.DeliverThing: no map for a drop pod"); return; }
                IntVec3 cell = BestDropCell(map, t, out string where);
                DropPodUtility.DropThingsNear(cell, map, new List<Thing> { t }, forbid: false);
                NotifyLanded(map, cell, $"{t.LabelCap}", where);
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"ColonyGoods.DeliverThing failed for {t.def?.defName}: {ex.Message}"); }
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
                List<Thing> stacks = MakeStacks(def, stuff, qualityIndex, qty);
                IntVec3 cell = BestDropCell(map, stacks.Count > 0 ? stacks[0] : null, out string where);
                DropPodUtility.DropThingsNear(cell, map, stacks, forbid: false);
                NotifyLanded(map, cell, $"{def.label} ×{qty}", where);
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"ColonyGoods drop pod failed for {def.defName} x{qty}: {ex.Message}"); }
        }

        // Where returned goods land, in preference order: a player-designated "KMH" stockpile (put "KMH" anywhere in a
        // stockpile's name, e.g. "KMH Depot", to pin every withdrawal there), then the best stockpile that ACCEPTS the
        // item (dumping stockpiles included, highest priority first), the trade-beacon drop spot, a home-area cell, and
        // only then the map center. DropThingsNear fine-tunes around the returned cell (avoids roofs/walls itself).
        private static IntVec3 BestDropCell(Map map, Thing sample, out string where)
        {
            try
            {
                if (map.zoneManager != null)
                {
                    // Designated landing zone: any stockpile named with "KMH" wins outright, so withdrawals land where
                    // the player chose instead of scattering to whatever zone is oldest. Highest-priority one first.
                    Zone_Stockpile kmhZone = null;
                    foreach (Zone z in map.zoneManager.AllZones)
                        if (z is Zone_Stockpile s && s.settings != null
                            && !string.IsNullOrEmpty(s.label) && s.label.IndexOf("KMH", StringComparison.OrdinalIgnoreCase) >= 0
                            && (kmhZone == null || s.settings.Priority > kmhZone.settings.Priority))
                            kmhZone = s;
                    if (kmhZone != null)
                        foreach (IntVec3 c in kmhZone.Cells)
                            if (ValidDropCell(map, c)) { where = $"your KMH zone '{kmhZone.label}'"; return c; }

                    if (sample != null)
                    {
                        Zone_Stockpile best = null;
                        foreach (Zone z in map.zoneManager.AllZones)
                            if (z is Zone_Stockpile s && s.settings != null && s.settings.AllowedToAccept(sample)
                                && (best == null || s.settings.Priority > best.settings.Priority))
                                best = s;
                        if (best != null)
                            foreach (IntVec3 c in best.Cells)
                                if (ValidDropCell(map, c)) { where = $"stockpile '{best.label}'"; return c; }
                    }
                }

                IntVec3 trade = DropCellFinder.TradeDropSpot(map);
                if (ValidDropCell(map, trade)) { where = "the trade drop spot"; return trade; }

                Area home = map.areaManager?.Home;
                if (home != null)
                    foreach (IntVec3 c in home.ActiveCells)
                        if (ValidDropCell(map, c)) { where = "your home area"; return c; }
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"ColonyGoods.BestDropCell failed: {ex.Message}"); }
            where = "the map center";
            return map.Center;
        }

        private static bool ValidDropCell(Map map, IntVec3 c)
            => c.IsValid && c.InBounds(map) && c.Standable(map) && !c.Fogged(map);

        // "Your items landed HERE" - clickable jump target; identical repeats within a few seconds collapse so a
        // multi-stack withdrawal doesn't stack a message per pod.
        private static DateTime _lastLandedMsgUtc = DateTime.MinValue;
        private static string   _lastLandedWhere  = "";
        private static void NotifyLanded(Map map, IntVec3 cell, string what, string where)
        {
            try
            {
                if (_lastLandedWhere == where && (DateTime.UtcNow - _lastLandedMsgUtc).TotalSeconds < 5) return;
                _lastLandedMsgUtc = DateTime.UtcNow; _lastLandedWhere = where;
                Messages.Message($"[KMH] {what} delivered to {where}.", new LookTargets(cell, map),
                    MessageTypeDefOf.PositiveEvent, historical: true);
            }
            catch { /* message is best-effort; the delivery already happened */ }
        }
    }
}
