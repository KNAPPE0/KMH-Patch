using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.UI
{
    internal static class ColonyGoods
    {
        public static ThingDef Silver => ThingDefOf.Silver;

        public static ThingDef Def(string defName)
            => string.IsNullOrWhiteSpace(defName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(defName);


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


        // All-or-nothing: too few and it removes nothing at all.
        public static bool TryRemove(Caravan caravan, ThingDef def, int qty)
        {
            if (caravan == null || def == null || qty <= 0) return false;

            List<Thing> matches;
            try { matches = CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => t?.def == def).ToList(); }
            catch { return false; }

            return RemoveExactFromCaravan(matches, qty);
        }

        public static bool TryRemoveSilver(Caravan caravan, int amount) => TryRemove(caravan, Silver, amount);


        // Returns false if the give threw, so a grant caller can flag charged-but-undelivered goods.
        public static bool Give(Caravan caravan, ThingDef def, int qty)
        {
            if (caravan == null || def == null || qty <= 0) return false;
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
                return true;
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"ColonyGoods.Give failed for {def.defName} x{qty}: {ex.Message}"); return false; }
        }

        public static bool GiveSilver(Caravan caravan, int amount) => Give(caravan, Silver, amount);

        public static bool Deliver(ThingDef def, int qty)
        {
            if (def == null || qty <= 0) return false;
            Caravan caravan = CaravanReader.GetSelectedCaravan();
            if (caravan != null) return Give(caravan, def, qty);
            return DropToHomeMap(def, qty);
        }

        public static bool DeliverSilver(int amount) => Deliver(Silver, amount);

        // Mid-load the World is null and AnyPlayerHomeMap NREs via FactionManager, which is when a replay arrives.
        internal static Map DeliveryMap()
        {
            try
            {
                if (Current.Game?.World == null) return null;
                return Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            }
            catch { return null; }
        }

        // Lets the retry hold an undeliverable grant until the player has somewhere to receive it.
        public static bool CanDeliverNow()
        {
            try { if (CaravanReader.GetSelectedCaravan() != null) return true; }
            catch { /* the world is not up either; the map check below is the real answer */ }
            return DeliveryMap() != null;
        }


        public static int CountKey(Caravan caravan, string key)
        {
            if (caravan == null || string.IsNullOrEmpty(key)) return 0;
            ItemKeys.Split(key, out string defName, out string stuff, out int q, out string inner);
            int total = 0;
            try
            {
                foreach (Thing t in CaravanInventoryUtility.AllInventoryItems(caravan))
                    if (MatchesKey(t, defName, stuff, q, inner)) total += t.stackCount;
            }
            catch { /* weird container - treat as 0 */ }
            return total;
        }

        // All-or-nothing, matching def + stuff + quality.
        public static bool TryRemoveKey(Caravan caravan, string key, int qty)
        {
            if (caravan == null || string.IsNullOrEmpty(key) || qty <= 0) return false;
            ItemKeys.Split(key, out string defName, out string stuff, out int q, out string inner);

            List<Thing> matches;
            try { matches = CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => MatchesKey(t, defName, stuff, q, inner)).ToList(); }
            catch { return false; }

            return RemoveExactFromCaravan(matches, qty);
        }

        // All-or-nothing; caravan items are unspawned, so a partial take only lowers the stack count.
        private static bool RemoveExactFromCaravan(List<Thing> matches, int qty)
        {
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

        // Checked BEFORE any destruction: a blob too big to send after removal is lost goods.
        public const int MaxPayloadBytesPerItem        = 40_000;
        public const int MaxPayloadBytesPerTransaction = 48_000;

        // Capture and validate EVERY piece before destroying any of them, or a partial failure loses goods.
        public static bool RemoveKeyCapturing(Caravan caravan, Map map, string key, int qty,
                                              out List<Items.KmhThingPayload> captured, out string refusal)
        {
            captured = new List<Items.KmhThingPayload>();
            refusal = null;
            if (string.IsNullOrEmpty(key) || qty <= 0) return false;
            ItemKeys.Split(key, out string defName, out string stuff, out int q, out string inner);

            List<Thing> matches;
            try
            {
                matches = caravan != null
                    ? CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => MatchesKey(t, defName, stuff, q, inner)).ToList()
                    : StoredOnMap(map, Def(defName)).Where(t => MatchesKey(t, defName, stuff, q, inner)).ToList();
            }
            catch { return false; }

            if (matches.Sum(t => t.stackCount) < qty) return false;

            // Partial takes are split off detached, so an abort can hand them straight back.
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
                capturedCount += Math.Max(1, p.StackCount);

                // The server keeps only the first blob per fungible entry, so shipping the rest buys nothing.
                if (Items.KmhThingCapture.TryFoldFungible(caps, p)) continue;

                totalBytes += bytes;
                if (totalBytes > MaxPayloadBytesPerTransaction) { ok = false; failReason = $"payloads total over per-transaction limit {MaxPayloadBytesPerTransaction}B"; break; }
                caps.Add(p);
            }
            if (ok && capturedCount != qty) { ok = false; failReason = $"captured {capturedCount} != requested {qty}"; }

            if (!ok)
            {
                foreach ((Thing piece, bool split) in taken) if (split) DeliverThing(piece);
                // Payload count is the diagnosis: a fungible def should fold to one, so a large number means it did not.
                Diagnostics.KmhLog.Warn($"KMH capture ABORTED for {defName} x{qty} - {failReason}; {caps.Count} payload(s) from {taken.Count} stack(s); items kept (nothing removed).");
                refusal = totalBytes > MaxPayloadBytesPerTransaction
                    ? $"Too much condition data in one deposit ({caps.Count} separate stacks). Deposit a smaller amount."
                    : "Could not take those items - nothing was removed.";
                captured = new List<Items.KmhThingPayload>();
                return false;
            }

            foreach ((Thing piece, bool _) in taken) piece.Destroy(DestroyMode.Vanish);
            captured = caps;
            return true;
        }

        public static bool DeliverPayloads(IEnumerable<Items.KmhThingPayload> payloads)
            => DeliverPayloads(payloads, out _, out _) == PayloadDelivery.Delivered;

        internal enum PayloadDelivery { Delivered, Blocked, Partial, Failed }

        // Every stack is rebuilt before any is placed, so one that cannot load never leaves others for a replay to place again.
        // Blocked names what is missing; Partial means some landed before a failure.
        internal static PayloadDelivery DeliverPayloads(IEnumerable<Items.KmhThingPayload> payloads, out string blocked, out string failed)
        {
            blocked = null; failed = null;
            if (payloads == null) return PayloadDelivery.Delivered;
            var built = new List<Thing>();
            var keyed = new List<(string key, int qty)>();
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
                        string key = ItemKeys.Compose(p.DefName, p.StuffDefName, p.Quality);
                        blocked = WhyCannotDeliverKey(key);
                        if (blocked != null) return PayloadDelivery.Blocked;
                        keyed.Add((key, remaining));   // legacy path splits into stacks itself
                        break;
                    }
                    int lim  = t.def != null && t.def.stackLimit > 0 ? t.def.stackLimit : remaining;
                    int give = Math.Min(remaining, lim);
                    t.stackCount = give;
                    built.Add(t);
                    remaining -= give;
                }
            }

            int placed = 0, lost = 0;
            foreach (Thing t in built) { if (DeliverThing(t)) placed++; else { lost++; failed = failed ?? t.LabelCap; } }
            foreach ((string key, int qty) in keyed) { if (DeliverKey(key, qty)) placed++; else { lost++; failed = failed ?? ItemKeys.LabelForKey(key); } }
            if (lost == 0) return PayloadDelivery.Delivered;
            return placed > 0 ? PayloadDelivery.Partial : PayloadDelivery.Failed;
        }

        // A permanent reason (not "no room") a key can never be built here, or null.
        internal static string WhyCannotDeliverKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "no item";
            ItemKeys.Split(key, out string defName, out string stuffName, out _);
            string missing = KmhItemDisplayService.MissingHere(defName, stuffName);
            if (missing != null) return missing;
            ThingDef def = Def(defName);
            if (Items.KmhItemSafety.IsMinifiedDef(def))
                return KmhText.Get("KMH_Item_CrateUnbuildable", "A crated building whose saved copy could not be loaded here.");
            if (!Items.KmhItemSafety.IsDeliverable(def))
                return KmhText.Get("KMH_Item_CannotLoad", "Its saved copy cannot be loaded in this game.");
            return null;
        }

        public static bool DeliverThing(Thing t)
        {
            if (t == null) return false;
            try
            {
                Caravan caravan = CaravanReader.GetSelectedCaravan();
                if (caravan != null) { CaravanInventoryUtility.GiveThing(caravan, t); return true; }
                Map map = DeliveryMap();
                if (map == null) { Diagnostics.KmhLog.Warn("ColonyGoods.DeliverThing: no map for a drop pod"); return false; }
                IntVec3 cell = BestDropCell(map, t, out string where);
                DropPodUtility.DropThingsNear(cell, map, new List<Thing> { t }, canRoofPunch: false, forbid: false);
                NotifyLanded(map, cell, $"{t.LabelCap}", where);
                return true;
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"ColonyGoods.DeliverThing failed for {t.def?.defName}: {ex.Message}"); return false; }
        }

        // An unknown def - a removed mod - fails rather than spawning something else.
        public static bool DeliverKey(string key, int qty)
        {
            if (string.IsNullOrEmpty(key) || qty <= 0) return false;
            ItemKeys.Split(key, out string defName, out string stuffName, out int q);
            ThingDef def = Def(defName);
            if (def == null) { Diagnostics.KmhLog.Warn($"ColonyGoods.DeliverKey: unknown def '{defName}'"); return false; }
            if (!Items.KmhItemSafety.IsDeliverable(def)) { Diagnostics.KmhLog.Warn($"ColonyGoods.DeliverKey: refused '{defName}' - not an item"); return false; }
            // def+stuff+quality yields a crate with nothing inside; refusing keeps the payload owed and retryable.
            if (Items.KmhItemSafety.IsMinifiedDef(def))
            {
                Diagnostics.KmhLog.Warn($"ColonyGoods.DeliverKey: refused to build minified '{defName}' from a key - it has no inner building.");
                return false;
            }
            if (TooManyStacks(def.stackLimit, qty))
            {
                Diagnostics.KmhLog.Warn($"ColonyGoods.DeliverKey: refused {qty} {defName} - more than {MaxStacksPerDelivery} stacks in one delivery.");
                return false;
            }
            ThingDef stuff = string.IsNullOrEmpty(stuffName) ? null : Def(stuffName);
            // Never another material: a steel copy of a bioferrite sword is a different item.
            if (!string.IsNullOrEmpty(stuffName) && stuff == null)
            {
                Diagnostics.KmhLog.Warn($"ColonyGoods.DeliverKey: unknown material '{stuffName}' for '{defName}'");
                return false;
            }

            Caravan caravan = CaravanReader.GetSelectedCaravan();
            if (caravan != null)
            {
                foreach (Thing t in MakeStacks(def, stuff, q, qty))
                    CaravanInventoryUtility.GiveThing(caravan, t);
                return true;
            }
            return DropToHomeMap(def, stuff, q, qty);
        }

        // Only stored, unforbidden items count: anything else is not the player's to deposit.
        public static Map DepositMap()
        {
            try
            {
                if (Current.Game?.World == null) return null;
                Map cur = Find.CurrentMap;
                if (cur != null && cur.IsPlayerHome) return cur;
                return Find.AnyPlayerHomeMap;
            }
            catch { return null; }
        }

        private static IEnumerable<Thing> StoredOnMap(Map map, ThingDef def)
        {
            if (map?.listerThings == null || def == null) yield break;
            foreach (Thing t in map.listerThings.ThingsOfDef(def))
                if (t != null && t.Spawned && t.IsInValidStorage()) yield return t;
        }

        public static int CountSilverOnMap(Map map) => CountOnMap(map, Silver);

        // Ignores material and quality on purpose; the composed-key pair below is what preserves them.
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
            ItemKeys.Split(key, out string defName, out string stuff, out int q, out string inner);
            ThingDef def = Def(defName);
            if (def == null) return 0;
            int total = 0;
            try { foreach (Thing t in StoredOnMap(map, def)) if (MatchesKey(t, defName, stuff, q, inner)) total += t.stackCount; }
            catch { /* weird storage - treat as 0 */ }
            return total;
        }

        public static bool TryRemoveSilverOnMap(Map map, int amount) => TryRemoveOnMap(map, Silver, amount);

        public static bool TryRemoveOnMapKey(Map map, string key, int qty)
        {
            if (map == null || string.IsNullOrEmpty(key) || qty <= 0) return false;
            ItemKeys.Split(key, out string defName, out string stuff, out int q, out string inner);
            ThingDef def = Def(defName);
            if (def == null) return false;
            List<Thing> matches;
            try { matches = StoredOnMap(map, def).Where(t => MatchesKey(t, defName, stuff, q, inner)).ToList(); }
            catch { return false; }
            return RemoveExactFromMap(matches, qty);
        }

        // All-or-nothing. SplitOff, not a raw stackCount decrement, keeps a spawned thing's reservations and UI consistent.
        private static bool RemoveExactFromMap(List<Thing> matches, int qty)
        {
            int total = matches.Sum(t => t.stackCount);
            if (total < qty) return false;
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

        public static Dictionary<string, int> ReadStoredInventory(Map map)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (map?.listerThings == null) return result;
            try
            {
                // HaulableEver is pre-indexed; AllThings would rescan filth, plants and buildings on every picker open.
                foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                {
                    if (t?.def == null || !t.Spawned || !t.IsInValidStorage()) continue;
                    if (t.def.category != ThingCategory.Item) continue;
                    string key = ItemKeys.KeyFor(t);
                    result[key] = (result.TryGetValue(key, out int cur) ? cur : 0) + t.stackCount;
                }
            }
            catch { /* rather show an empty picker than crash the dialog */ }
            return result;
        }

        // innerDefName pins which minified building this is; they all share one minifiedDef.
        private static bool MatchesKey(Thing t, string defName, string stuffName, int qualityIndex, string innerDefName)
        {
            if (t?.def == null || !string.Equals(t.def.defName, defName, StringComparison.Ordinal)) return false;
            string ts = t.Stuff?.defName ?? "";
            if (!string.Equals(ts, stuffName ?? "", StringComparison.Ordinal)) return false;
            if (ItemKeys.QualityIndexOf(t) != qualityIndex) return false;
            string inner = (t as MinifiedThing)?.InnerThing?.def?.defName ?? "";
            return string.Equals(inner, innerDefName ?? "", StringComparison.Ordinal);
        }

        // Far past any real withdrawal (100k silver is 200 stacks); a server's amount must not build millions of things.
        internal const int MaxStacksPerDelivery = 1000;

        internal static bool TooManyStacks(int stackLimit, int qty)
            => ((long)qty + Math.Max(1, stackLimit) - 1) / Math.Max(1, stackLimit) > MaxStacksPerDelivery;

        private static List<Thing> MakeStacks(ThingDef def, ThingDef stuff, int qualityIndex, int qty)
        {
            List<Thing> things = new List<Thing>();
            int remaining = qty, stackLimit = Math.Max(1, def.stackLimit);
            if (TooManyStacks(stackLimit, qty))
                throw new InvalidOperationException($"{qty} {def.defName} would be more than {MaxStacksPerDelivery} stacks in one delivery");
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

        private static bool DropToHomeMap(ThingDef def, int qty) => DropToHomeMap(def, null, 0, qty);

        private static bool DropToHomeMap(ThingDef def, ThingDef stuff, int qualityIndex, int qty)
        {
            try
            {
                Map map = DeliveryMap();
                if (map == null) { Diagnostics.KmhLog.Warn("ColonyGoods.Deliver: no map for a drop pod"); return false; }
                List<Thing> stacks = MakeStacks(def, stuff, qualityIndex, qty);
                IntVec3 cell = BestDropCell(map, stacks.Count > 0 ? stacks[0] : null, out string where);
                DropPodUtility.DropThingsNear(cell, map, stacks, canRoofPunch: false, forbid: false);
                NotifyLanded(map, cell, $"{def.label} ×{qty}", where);
                return true;
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"ColonyGoods drop pod failed for {def.defName} x{qty}: {ex.Message}"); return false; }
        }

        private static IntVec3 BestDropCell(Map map, Thing sample, out string where)
        {
            try
            {
                if (map.zoneManager != null)
                {
                    // A "KMH" stockpile wins outright, so withdrawals land where the player chose.
                    Zone_Stockpile kmhZone = null;
                    foreach (Zone z in map.zoneManager.AllZones)
                        if (z is Zone_Stockpile s && s.settings != null
                            && IsDeliveryStockpileName(s.label)
                            && (kmhZone == null || s.settings.Priority > kmhZone.settings.Priority))
                            kmhZone = s;
                    if (kmhZone != null && PickDropCell(kmhZone.Cells, c => ValidDropCell(map, c), c => c.Roofed(map), out IntVec3 kc, out bool kRoofed))
                    { where = kRoofed ? $"beside your stockpile '{kmhZone.label}' (it is roofed)" : $"your stockpile '{kmhZone.label}'"; return kc; }

                    if (sample != null)
                    {
                        Zone_Stockpile best = null;
                        foreach (Zone z in map.zoneManager.AllZones)
                            if (z is Zone_Stockpile s && s.settings != null && s.settings.AllowedToAccept(sample)
                                && (best == null || s.settings.Priority > best.settings.Priority))
                                best = s;
                        if (best != null && PickDropCell(best.Cells, c => ValidDropCell(map, c), c => c.Roofed(map), out IntVec3 bc, out bool bRoofed))
                        { where = bRoofed ? $"beside stockpile '{best.label}' (it is roofed)" : $"stockpile '{best.label}'"; return bc; }
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

        // "Nexus" is the name players are told to use; "KMH" keeps stockpiles named before the rebrand working.
        internal static bool IsDeliveryStockpileName(string label)
            => !string.IsNullOrEmpty(label)
               && (label.IndexOf("Nexus", StringComparison.OrdinalIgnoreCase) >= 0
                   || label.IndexOf("KMH", StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool ValidDropCell(Map map, IntVec3 c)
            => c.IsValid && c.InBounds(map) && c.Standable(map) && !c.Fogged(map);

        // An open cell first: a pod aimed under a roof lands beside it, since every drop here forbids punching through.
        internal static bool PickDropCell(IEnumerable<IntVec3> cells, Func<IntVec3, bool> valid, Func<IntVec3, bool> roofed, out IntVec3 cell, out bool isRoofed)
        {
            cell = IntVec3.Invalid; isRoofed = false;
            foreach (IntVec3 c in cells)
            {
                if (!valid(c)) continue;
                if (!roofed(c)) { cell = c; isRoofed = false; return true; }
                if (!cell.IsValid) { cell = c; isRoofed = true; }
            }
            return cell.IsValid;
        }

        // Repeats collapse, or a multi-stack withdrawal posts one message per pod.
        private static DateTime _lastLandedMsgUtc = DateTime.MinValue;
        private static string   _lastLandedWhere  = "";
        private static void NotifyLanded(Map map, IntVec3 cell, string what, string where)
        {
            try
            {
                if (_lastLandedWhere == where && (DateTime.UtcNow - _lastLandedMsgUtc).TotalSeconds < 5) return;
                _lastLandedMsgUtc = DateTime.UtcNow; _lastLandedWhere = where;
                Messages.Message($"[Colony Nexus] {what} delivered to {where}.", new LookTargets(cell, map),
                    MessageTypeDefOf.PositiveEvent, historical: true);
            }
            catch { /* message is best-effort; the delivery already happened */ }
        }
    }
}
