using KMHPatch.Items;
using RimWorld;
using Verse;

namespace KMHPatch.UI
{
    // One façade for showing an item (label/icon/decision/tooltip) so every dialog resolves it identically.
    internal static class KmhItemDisplayService
    {
        public static string Label(string defName) => ItemLabels.ResolveLabel(defName);
        public static string StuffedLabel(string itemDefName, string stuffDefName) => ItemLabels.ResolveStuffedLabel(itemDefName, stuffDefName);

        // An unknown def is blocked, as in the capture path.
        public static KmhItemDecision DecisionFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return KmhItemDecision.Block(KmhItemReasonCode.MissingDef, "no item");
            ItemKeys.Split(key, out string defName, out string stuff, out _, out string inner);
            ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (td == null) return KmhItemDecision.Block(KmhItemReasonCode.MissingDef, "unknown item (def not loaded)");
            string missing = MissingHere(defName, stuff, inner);
            return missing != null ? KmhItemDecision.Block(KmhItemReasonCode.MissingDef, missing) : KmhItemSafety.EvaluateDef(td);
        }

        public static string MissingHere(string defName, string stuffDefName = null, string innerDefName = null)
        {
            if (string.IsNullOrEmpty(defName)) return "no item";
            return Absent(defName) ?? Absent(stuffDefName) ?? Absent(innerDefName);
        }

        private static string Absent(string name)
            => string.IsNullOrEmpty(name) || DefDatabase<ThingDef>.GetNamedSilentFail(name) != null ? null
             : KmhText.Get("KMH_Item_NeedsContent", "Needs {0}, which this game does not have: a DLC or mod that is not active here.", KmhDisplayText.Inert(name, 80));

        // Per listing or auction id: rows draw every frame, and what this game has loaded does not change mid-game.
        private static readonly System.Collections.Generic.Dictionary<long, string> _listingVerdicts =
            new System.Collections.Generic.Dictionary<long, string>();

        // Auctions and listings number separately, so an auction's id is kept apart by its sign.
        public static string MissingForListingCached(bool auction, long id, string itemKey, string stuffDefName,
                                                     System.Collections.Generic.IEnumerable<KmhThingPayload> payloads)
        {
            long key = auction ? -id - 1 : id;
            if (_listingVerdicts.TryGetValue(key, out string known)) return known;
            string why = MissingForListing(itemKey, stuffDefName, payloads);
            if (_listingVerdicts.Count > 4096) _listingVerdicts.Clear();
            _listingVerdicts[key] = why;
            return why;
        }

        // Keyed on the state fingerprint: rows draw every frame, and a def database does not change mid-game.
        private static readonly System.Collections.Generic.Dictionary<string, string> _payloadVerdicts =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);

        public static string MissingForPayload(KmhThingPayload p)
        {
            if (p == null) return null;
            string cacheKey = string.IsNullOrEmpty(p.Fingerprint) ? null : p.Fingerprint;
            if (cacheKey != null && _payloadVerdicts.TryGetValue(cacheKey, out string known)) return known;
            string why = MissingHere(p.DefName, p.StuffDefName);
            if (why == null && !string.IsNullOrEmpty(p.ScribeXml)
                && !KmhThingScribe.ShapeMatches(p.ScribeXml, p.DefName, d => DefDatabase<ThingDef>.GetNamedSilentFail(d)?.thingClass,
                                                n => GenTypes.GetTypeInAnyAssembly(n), out string refused))
                why = refused != null && refused.Contains("not loaded")
                    ? KmhText.Get("KMH_Item_NeedsContentPart", "Needs content this game does not have ({0}): a DLC or mod that is not active here.", refused)
                    : KmhText.Get("KMH_Item_CannotLoad", "Its saved copy cannot be loaded in this game.");
            if (cacheKey != null) _payloadVerdicts[cacheKey] = why;
            return why;
        }

        // For a listing or auction: the item key, its material, and every escrowed copy must exist here.
        public static string MissingForListing(string itemKey, string stuffDefName, System.Collections.Generic.IEnumerable<KmhThingPayload> payloads)
        {
            ItemKeys.Split(itemKey ?? "", out string defName, out string keyStuff, out _, out string inner);
            string why = MissingHere(defName, string.IsNullOrEmpty(stuffDefName) ? keyStuff : stuffDefName, inner);
            if (why != null || payloads == null) return why;
            foreach (KmhThingPayload p in payloads) { why = MissingForPayload(p); if (why != null) return why; }
            return null;
        }

        public static bool IsAllowed(string defName) => DecisionFor(defName).Allowed;
        public static string Tooltip(string defName) => KmhItemTooltipBuilder.For(defName);
    }
}
