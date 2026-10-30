using System;
using System.Collections.Generic;
using System.Text;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Shown after the profile arrives and before any write, so consent covers the files themselves.
    public class Dialog_KMHEnforcementReview : Window
    {
        private readonly List<EnforcementProfileApplier.ReviewItem> _items;
        private readonly Mods.KmhModPlan _plan;
        private readonly Action<bool> _answer;
        private bool _answered;
        private Vector2 _scroll;
        public override Vector2 InitialSize => DialogLayout.FitToScreen(600f, 480f);

        internal Dialog_KMHEnforcementReview(List<EnforcementProfileApplier.ReviewItem> items, Mods.KmhModPlan plan, Action<bool> answer)
        {
            _items = items ?? new List<EnforcementProfileApplier.ReviewItem>();
            _plan = plan;
            _answer = answer;
            forcePause              = true;
            doCloseX                = false;
            absorbInputAroundWindow = true;
            closeOnAccept           = false;
            closeOnCancel           = false;
            closeOnClickedOutside   = false;
            draggable               = false;
        }

        public override void OnCancelKeyPressed() { }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), KmhText.Get("KMH_Review_Title", "What this server's settings change"));
            Text.Font = GameFont.Small;

            Rect body = new Rect(0f, 40f, inRect.width, inRect.height - 40f - 44f);
            string text = Summary(_items, _plan);
            float height = Text.CalcHeight(text, body.width - 18f);
            Rect view = new Rect(0f, 0f, body.width - 18f, Mathf.Max(height, body.height));
            Widgets.BeginScrollView(body, ref _scroll, view);
            Widgets.Label(view, text);
            Widgets.EndScrollView();

            float by = inRect.height - 36f, bw = (inRect.width - 12f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, by, bw, 32f), KmhText.Get("KMH_Review_Apply", "Apply these settings"))) Answer(true);
            if (Widgets.ButtonText(new Rect(bw + 12f, by, bw, 32f), KmhText.Get("KMH_Consent_Disconnect", "Disconnect"))) Answer(false);
        }

        private void Answer(bool ok)
        {
            if (_answered) return;
            _answered = true;
            Close();
            try { _answer?.Invoke(ok); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"Enforcement: the review answer failed: {ex.Message}"); }
        }

        // Mod names come from the player's own installed mods; file names come from the server, so both are inert.
        internal static string Summary(List<EnforcementProfileApplier.ReviewItem> items, Mods.KmhModPlan plan = null)
        {
            var sb = new StringBuilder();
            sb.Append(KmhText.Get("KMH_Review_Intro", "Colony Nexus has the server's settings and has not written anything yet. Your own copies are backed up first and can be put back at any time from the Colony Nexus tab, Mod Options, or the main menu.")).Append("\n\n");
            // The mod list is written in the same step, so it is shown again here.
            if (plan != null)
            {
                sb.Append("<b>").Append(KmhText.Get("KMH_Review_ModList", "Your mod list")).Append("</b>\n");
                int before = sb.Length;
                Dialog_KMHEnforcementConsent.AppendSwitches(sb, plan);
                if (sb.Length == before) sb.Append("  ").Append(KmhText.Get("KMH_Review_ModListSame", "No mod is switched on or off.")).Append('\n');
                sb.Append('\n');
            }
            Group(sb, items, EnforcementProfileApplier.ReviewKind.Replaces, KmhText.Get("KMH_Review_Replaces", "Replaces your settings for"));
            Group(sb, items, EnforcementProfileApplier.ReviewKind.Merges, KmhText.Get("KMH_Review_Merges", "Sets the gameplay settings, keeping your personal ones, for"));
            Group(sb, items, EnforcementProfileApplier.ReviewKind.Adds, KmhText.Get("KMH_Review_Adds", "Adds settings for"));
            int unused = items.FindAll(i => i.Kind == EnforcementProfileApplier.ReviewKind.Unused).Count;
            if (unused > 0)
                sb.Append(KmhText.Get("KMH_Review_Unused", "{0} file(s) are for mods you do not have running; they are stored but nothing reads them.", unused)).Append("\n\n");
            if (items.Count == 0)
                sb.Append(KmhText.Get("KMH_Review_Nothing", "Every file already matches what you have.")).Append("\n\n");
            if (items.Count > unused)
                sb.Append(KmhText.Get("KMH_Review_Restart", "RimWorld restarts once afterwards so every mod reads its new settings."));
            return sb.ToString();
        }

        private static void Group(StringBuilder sb, List<EnforcementProfileApplier.ReviewItem> items, EnforcementProfileApplier.ReviewKind kind, string heading)
        {
            var byMod = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (EnforcementProfileApplier.ReviewItem i in items)
                if (i.Kind == kind) byMod[i.Mod] = byMod.TryGetValue(i.Mod, out int n) ? n + 1 : 1;
            if (byMod.Count == 0) return;
            sb.Append("<b>").Append(heading).Append("</b>\n");
            const int Max = 40;
            int shown = 0;
            foreach (KeyValuePair<string, int> m in byMod)
            {
                if (shown++ == Max) { sb.Append("  ").Append(KmhText.Get("KMH_Review_More", "and {0} more mod(s)", byMod.Count - Max)).Append('\n'); break; }
                sb.Append("  • ").Append(KmhDisplayText.Inert(m.Key, 64));
                if (m.Value > 1) sb.Append(" (").Append(m.Value).Append(')');
                sb.Append('\n');
            }
            sb.Append('\n');
        }
    }
}
