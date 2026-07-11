using KMHPatch.Items;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Draws one item row the same way in every list: icon + label + optional count, greyed with a reason tooltip when
    // the shared safety layer blocks the def. Dialogs call this instead of hand-rolling icon/label/greyout logic.
    internal static class KmhItemRow
    {
        public const float IconSize = 22f;

        // Draws the icon+label into `row`, leaving `rightReserve` px on the right for the caller's button/count.
        // Returns the item's decision so the caller can enable/disable its action button consistently.
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

        // One full-state (payload) stack row: icon + rich label + state + count. No safety greyout - these are
        // already-owned vault stacks, and the server re-verifies every withdraw anyway.
        public static void DrawPayload(Rect row, KmhThingPayload p, float rightReserve)
        {
            KmhIconResolver.Draw(new Rect(row.x + 6f, row.y + 4f, IconSize, IconSize), p.DefName);
            Rect labelRect = new Rect(row.x + 6f + IconSize + 8f, row.y + 6f, row.width - rightReserve - IconSize - 24f, row.height - 12f);
            DialogLayout.LabelTrunc(labelRect, $"{PayloadLabel(p)}{PayloadSuffix(p)}   <color=grey>x{p.StackCount}</color>");
        }

        public static string PayloadLabel(KmhThingPayload p)
            => string.IsNullOrEmpty(p?.DisplayLabel) ? ItemLabels.ResolveLabel(p?.DefName ?? "") : p.DisplayLabel;

        // Short "(plasteel, q5, 40/60hp, tainted, legacy)" state suffix for a full-state payload stack.
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
