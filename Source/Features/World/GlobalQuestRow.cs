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
        // Tall enough for the body font: the progress text sits inside this bar and is unreadable at Tiny.
        private const float BarH  = 22f;
        private const float Gap   = 4f;
        private const float MinBar = 60f;   // below this the progress bar stops meaning anything

        // Measured: LabelTrunc centre-grows a rect shorter than a font line, so a fixed height misplaces the title.
        private static float LineH => Mathf.Ceil(Text.LineHeight);
        private static float Row2H => Mathf.Max(BarH, LineH);
        public  static float Height => LineH + Gap + Row2H + Gap;

        public static void Draw(Rect row, ServerQuestDto q, string me, long now)
        {
            bool   comp    = string.Equals(q.Kind, ServerQuestDto.KindCompetitive, StringComparison.OrdinalIgnoreCase);
            bool   deliver = string.Equals(q.Objective, ServerQuestDto.ObjDeliver, StringComparison.OrdinalIgnoreCase);
            bool   build   = string.Equals(q.Objective, ServerQuestDto.ObjBuild, StringComparison.OrdinalIgnoreCase);
            string kindTag = comp ? "<color=#F5C242>" + KmhText.Get("KMH_World_Race", "RACE") + "</color>"
                                  : "<color=#4cc38a>" + KmhText.Get("KMH_World_Coop", "CO-OP") + "</color>";
            string reward  = q.RewardPool > 0 ? $"<color=yellow>{SilverFmt.Format(q.RewardPool)}</color>"
                                              : "<color=#A9AFB9>" + KmhText.Get("KMH_World_Glory", "glory") + "</color>";
            string time    = q.EndsUtcTicks > 0 ? $"  <color=#A9AFB9>•</color>  {DialogLayout.TimeRemainingShort(q.EndsUtcTicks, now)}" : "";

            DialogLayout.LabelTrunc(new Rect(row.x, row.y, row.width, LineH),
                $"[{kindTag}] <b>{q.Title}</b>  <color=#A9AFB9>•</color> {reward}{time}");

            int mine = 0;
            if (!string.IsNullOrEmpty(me) && q.Contributors != null) q.Contributors.TryGetValue(me, out mine);
            float pct = q.GoalQty > 0 ? Mathf.Clamp01((float)q.ProgressQty / q.GoalQty) : 0f;

            // At split-screen widths the right column yields to the bar, and the button drops once neither fits.
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
                // The game's own label: "Meat_Horse" is a def name, not something a player should read.
                string targetLabel = deliver ? UI.ItemLabels.ResolveLabel(q.TargetDefName)
                                             : DefDatabase<PawnKindDef>.GetNamedSilentFail(q.TargetDefName ?? "")?.label ?? q.TargetDefName;
                // One sentence per objective, so a translation can put the verb after the target.
                string progress = build   ? KmhText.Get("KMH_World_ProgressBuild", "{0}/{1} Build {2}", q.ProgressQty, q.GoalQty, targetLabel)
                                : deliver ? KmhText.Get("KMH_World_ProgressDeliver", "{0}/{1} Deliver {2}", q.ProgressQty, q.GoalQty, targetLabel)
                                :           KmhText.Get("KMH_World_ProgressHunt", "{0}/{1} Hunt {2}", q.ProgressQty, q.GoalQty, targetLabel);
                DialogLayout.LabelTrunc(new Rect(bar.x + 4f, bar.y, Mathf.Max(0f, bar.width - 8f), bar.height),
                                        progress, TextAnchor.MiddleCenter);
                Text.Font = pf;
            }

            float btnW  = showBtn ? 66f : 0f;
            float youX  = bar.xMax + 8f;
            float youW  = Mathf.Max(0f, row.xMax - btnW - (showBtn ? 6f : 0f) - youX);
            if (youW > 12f)
            {
                Color old = GUI.color;
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(youX, row2Y, youW, Row2H),
                    mine > 0 ? KmhText.Get("KMH_World_You", "you: {0}", "<color=white>" + mine.ToString() + "</color>")
                             : "<color=#A9AFB9>" + KmhText.Get("KMH_World_YouNone", "you: 0") + "</color>");
                GUI.color = old;
            }

            if (!showBtn) return;

            Rect btn = new Rect(row.xMax - btnW, row2Y + (Row2H - 20f) / 2f, btnW, 20f);
            string deliverLabel = KmhText.Get("KMH_Want_Deliver", "Deliver");
            if (!Widgets.ButtonText(btn, deliverLabel)) return;

            long   id     = q.Id;
            string target = q.TargetDefName;
            var    def    = ColonyGoods.Def(target);
            // The server pays this out of the treasury, so the cap is what the treasury holds - not the colony.
            int    have   = WorldHandler.TreasuryStockOf(target);
            if (have <= 0)
            {
                KmhNotifications.Rejected(KmhText.Get("KMH_World_DepositFirst", "Deposit {0} into your treasury first - deliveries are paid out of it.",
                    def != null ? def.label : target));
                return;
            }
            Find.WindowStack.Add(new Dialog_KMHAmountInput(
                title:        KmhText.Get("KMH_World_DeliverTo", "Deliver to {0}", q.Title),
                confirmLabel: deliverLabel,
                unitLabel:    def != null ? def.label : target,
                maxHint:      have,
                onConfirm:    n => WorldHandler.TryDeliver(id, target, n)));
        }
    }
}
