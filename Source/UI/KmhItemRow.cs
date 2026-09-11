using KMHPatch.Items;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    internal static class KmhItemRow
    {
        public const float IconSize = 22f;

        // Returns the decision so the caller's action button greys out for the same reason the row does.
        public static KmhItemDecision Draw(Rect row, string defName, int count, float rightReserve)
        {
            KmhItemDecision d = KmhItemDisplayService.DecisionFor(defName);

            Color prev = GUI.color;
            if (!d.Allowed) GUI.color = new Color(1f, 1f, 1f, 0.45f);   // grey blocked rows, keep them readable

            KmhIconResolver.Draw(new Rect(row.x + 6f, row.y + 4f, IconSize, IconSize), defName);

            string label = KmhItemDisplayService.Label(defName);
            string countText = count > 0 ? $"   <color=grey>x{count}</color>" : "";
            Rect labelRect = new Rect(row.x + 6f + IconSize + 8f, row.y + 6f, row.width - rightReserve - IconSize - 24f, row.height - 12f);
            DialogLayout.LabelTrunc(labelRect, $"{label}{countText}");
            GUI.color = prev;

            if (Mouse.IsOver(row)) TooltipHandler.TipRegion(row, KmhItemTooltipBuilder.For(defName, d));
            return d;
        }

        // No safety greyout: these are already-owned vault stacks and the server re-verifies every withdraw.
        public static void DrawPayload(Rect row, KmhThingPayload p, float rightReserve)
        {
            KmhIconResolver.Draw(new Rect(row.x + 6f, row.y + 4f, IconSize, IconSize), p.DefName);
            Rect labelRect = new Rect(row.x + 6f + IconSize + 8f, row.y + 6f, row.width - rightReserve - IconSize - 24f, row.height - 12f);
            DialogLayout.LabelTrunc(labelRect, $"{PayloadLabel(p)}{PayloadSuffix(p)}   <color=grey>x{p.StackCount}</color>");
        }

        public static string PayloadLabel(KmhThingPayload p)
            => string.IsNullOrEmpty(p?.DisplayLabel) ? ItemLabels.ResolveLabel(p?.DefName ?? "") : p.DisplayLabel;

        public static string PayloadSuffix(KmhThingPayload p)
        {
            var bits = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(p.StuffDefName)) bits.Add(p.StuffDefName);
            if (p.Quality > 0) bits.Add("q" + p.Quality);
            if (p.HitPoints >= 0 && p.MaxHitPoints > 0 && p.HitPoints < p.MaxHitPoints) bits.Add($"{p.HitPoints}/{p.MaxHitPoints}hp");
            if (p.Tainted) bits.Add("tainted");
            if (p.Legacy) bits.Add("legacy");
            else if (string.Equals(p.Fidelity, KmhThingPayload.FidelityMetadata, System.StringComparison.OrdinalIgnoreCase)) bits.Add("partial");
            return bits.Count > 0 ? " <color=grey>(" + string.Join(", ", bits) + ")</color>" : "";
        }
    }
}
