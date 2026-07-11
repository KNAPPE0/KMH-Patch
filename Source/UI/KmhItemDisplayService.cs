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

        // Safety decision for a key (plain or composed def|stuff|quality). Unknown def -> blocked, as in the capture path.
        public static KmhItemDecision DecisionFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return KmhItemDecision.Block(KmhItemReasonCode.MissingDef, "no item");
            ItemKeys.Split(key, out string defName, out _, out _);
            ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            return td == null
                ? KmhItemDecision.Block(KmhItemReasonCode.MissingDef, "unknown item (def not loaded)")
                : KmhItemSafety.EvaluateDef(td);
        }

        public static bool IsAllowed(string defName) => DecisionFor(defName).Allowed;
        public static string Tooltip(string defName) => KmhItemTooltipBuilder.For(defName);
    }
}
