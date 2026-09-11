using System;
using KMHPatch.Features.World.Dto;
using KMHPatch.Notifications;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.World
{
    // Shared by the Quest Board banner and the World dialog, so the two can never show a quest differently.
    internal static class GlobalQuestRow
    {
        // Tall enough for the body font: the progress text sits INSIDE this bar, and at Tiny it was unreadable at 1080p.
        private const float BarH  = 22f;
        private const float Gap   = 4f;
        private const float MinBar = 60f;   // below this the progress bar stops meaning anything

        // Measured, never fixed: LabelTrunc centre-grows any rect shorter than a font line, so a hardcoded height drew the title above its row and clipped it.
        private static float LineH => Mathf.Ceil(Text.LineHeight);
        private static float Row2H => Mathf.Max(BarH, LineH);
        public  static float Height => LineH + Gap + Row2H + Gap;

        public static void Draw(Rect row, ServerQuestDto q, string me, long now)
        {
            bool   comp    = string.Equals(q.Kind, ServerQuestDto.KindCompetitive, StringComparison.OrdinalIgnoreCase);
            bool   deliver = string.Equals(q.Objective, ServerQuestDto.ObjDeliver, StringComparison.OrdinalIgnoreCase);
            string kindTag = comp ? "<color=#F5C242>RACE</color>" : "<color=#7CD37C>CO-OP</color>";
            string objVerb = string.Equals(q.Objective, ServerQuestDto.ObjBuild, StringComparison.OrdinalIgnoreCase) ? "Build"
                           : deliver ? "Deliver" : "Hunt";
            string reward  = q.RewardPool > 0 ? $"<color=yellow>{SilverFmt.Format(q.RewardPool)}</color>" : "<color=grey>glory</color>";
            string time    = q.EndsUtcTicks > 0 ? $"  <color=grey>•</color>  {DialogLayout.TimeRemainingShort(q.EndsUtcTicks, now)}" : "";

            DialogLayout.LabelTrunc(new Rect(row.x, row.y, row.width, LineH),
                $"[{kindTag}] <b>{q.Title}</b>  <color=grey>•</color> {reward}{time}");

            int mine = 0;
            if (!string.IsNullOrEmpty(me) && q.Contributors != null) q.Contributors.TryGetValue(me, out mine);
            float pct = q.GoalQty > 0 ? Mathf.Clamp01((float)q.ProgressQty / q.GoalQty) : 0f;

            // At split-screen widths the right column yields to the bar rather than pushing it off the edge, and the button drops once neither fits.
            float row2Y   = row.y + LineH + Gap;
            float wantR   = deliver ? 168f : 110f;
            float rightW  = Mathf.Min(wantR, Mathf.Max(0f, row.width - MinBar));
            bool  showBtn = deliver && rightW >= 120f;
            if (deliver && !showBtn) rightW = Mathf.Min(110f, Mathf.Max(0f, row.width - MinBar));

            Rect bar = new Rect(row.x, row2Y + (Row2H - BarH) / 2f, Mathf.Max(0f, row.width - rightW), BarH);
            if (bar.width > 0f)
            {
                Widgets.DrawBoxSolid(bar, new Color(0.16f, 0.16f, 0.16f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * pct, bar.height),
                    comp ? new Color(0.96f, 0.76f, 0.26f) : new Color(0.34f, 0.55f, 0.45f));

                GameFont   pf = Text.Font;   Text.Font   = GameFont.Small;
                TextAnchor pa = Text.Anchor; Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(bar, $"{q.ProgressQty}/{q.GoalQty} {objVerb} {q.TargetDefName}");
                Text.Anchor = pa; Text.Font = pf;
            }

            float btnW  = showBtn ? 66f : 0f;
            float youX  = bar.xMax + 8f;
            float youW  = Mathf.Max(0f, row.xMax - btnW - (showBtn ? 6f : 0f) - youX);
            if (youW > 12f)
            {
                Color old = GUI.color;
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(youX, row2Y, youW, Row2H),
                    mine > 0 ? $"you: <color=white>{mine}</color>" : "<color=grey>you: 0</color>");
                GUI.color = old;
            }

            if (!showBtn) return;

            Rect btn = new Rect(row.xMax - btnW, row2Y + (Row2H - 20f) / 2f, btnW, 20f);
            if (!Widgets.ButtonText(btn, "Deliver")) return;

            long   id     = q.Id;
            string target = q.TargetDefName;
            var    def    = ColonyGoods.Def(target);
            // The server pays this out of the treasury, so the cap is what the treasury holds - not the colony.
            int    have   = WorldHandler.TreasuryStockOf(target);
            if (have <= 0)
            {
                KmhNotifications.Rejected($"Deposit {(def != null ? def.label : target)} into your treasury first - deliveries are paid out of it.");
                return;
            }
            Find.WindowStack.Add(new Dialog_KMHAmountInput(
                title:        $"Deliver to {q.Title}",
                confirmLabel: "Deliver",
                unitLabel:    def != null ? def.label : target,
                maxHint:      have,
                onConfirm:    n => WorldHandler.TryDeliver(id, target, n)));
        }
    }
}
