using KMHPatch.Items;
using RimWorld;
using Verse;

namespace KMHPatch.UI
{
    // Builds the hover tooltip for an item row: friendly name + (when blocked) the shared safety reason, so a greyed
    // row always explains itself with the SAME wording everywhere.
    internal static class KmhItemTooltipBuilder
    {
        public static string For(string defName, KmhItemDecision decision)
        {
            string label = ItemLabels.ResolveLabel(defName);
            if (decision.Allowed)
            {
                ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                string desc = td?.description;
                return string.IsNullOrEmpty(desc) ? label : $"{label}\n\n{desc}";
            }
            return $"{label}\n\n<color=#ff9a9a>Blocked: {decision.Reason}</color>";
        }

        public static string For(string defName)
            => For(defName, KmhItemDisplayService.DecisionFor(defName));
    }
}
