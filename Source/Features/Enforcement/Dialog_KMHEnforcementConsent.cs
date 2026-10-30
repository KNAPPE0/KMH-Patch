using System;
using UnityEngine;
using KMHPatch.UI;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // One dialog for configs and mod list: they apply only as a whole, so consent must not be split in two.
    public class Dialog_KMHEnforcementConsent : Window
    {
        private readonly Action _onClose;
        private Vector2 _scroll;
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(600f, 480f);

        public Dialog_KMHEnforcementConsent(Action onClose)
        {
            _onClose                = onClose;
            forcePause              = true;
            doCloseX                = false;
            absorbInputAroundWindow = true;
            closeOnAccept           = false;
            closeOnCancel           = false;
            closeOnClickedOutside   = false;
            draggable               = false;
        }

        public override void OnCancelKeyPressed() { /* swallow Escape - the player must choose */ }
        public override void PostClose() { base.PostClose(); try { _onClose?.Invoke(); } catch { } }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), KmhText.Get("KMH_Consent_Title", "Before you can play here"));
            Text.Font = GameFont.Small;

            Rect body = new Rect(0f, 40f, inRect.width, inRect.height - 40f - 44f);
            // Read once: Apply agrees to exactly the list drawn here, even if a snapshot replaces it mid-frame.
            System.Collections.Generic.List<string> shown = EnforcementCache.ModOrder;
            string text = BuildSummary(shown);
            float height = Text.CalcHeight(text, body.width - 18f);
            Rect view = new Rect(0f, 0f, body.width - 18f, Mathf.Max(height, body.height));
            Widgets.BeginScrollView(body, ref _scroll, view);
            Widgets.Label(view, text);
            Widgets.EndScrollView();

            float by = inRect.height - 36f;
            float bw = (inRect.width - 12f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, by, bw, 32f), KmhText.Get("KMH_Consent_Apply", "Apply & continue")))
            {
                EnforcementFlow.ApplyNow(EnforcementCache.OrderFingerprint(shown));
                Close();
            }
            if (Widgets.ButtonText(new Rect(bw + 12f, by, bw, 32f), KmhText.Get("KMH_Consent_Disconnect", "Disconnect")))
            {
                EnforcementFlow.DeclineAndDisconnect();
                Close();
            }
        }

        internal static string BuildSummary() => BuildSummary(EnforcementCache.ModOrder);

        internal static string BuildSummary(System.Collections.Generic.List<string> want)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            bool settings = EnforcementCache.HasProfile;
            bool order = want != null && want.Count > 0;

            sb.Append(KmhText.Get("KMH_Consent_Intro", "This server requires the following before the world loads.") + "\n\n");

            if (settings)
                sb.Append("<b>Mod settings</b>\nYour current configs are backed up first, then this server's "
                        + "profile is applied. You can restore yours later from the Colony Nexus tab, Mod Options, or the "
                        + "main menu. " + KmhText.Get("KMH_Consent_ReviewNext", "Before anything is written you are shown which mods' settings change.") + "\n\n");

            if (order)
            {
                sb.Append("<b>Mod list and load order</b>\n");
                Features.Global.ModProfileDto profile = Features.Global.ModProfileClient.Peek(
                    SubProtocol.KmhDispatcher.ServerId)?.Profile;
                Features.Global.ModComparison c = profile == null ? null
                    : Features.Global.ModComparison.Compare(profile, Features.Global.KmhLocalMods.ActiveNames());

                if (c != null && c.Missing.Count > 0)
                    sb.Append("  Missing, and you must install them yourself: ")
                      .Append(Join(c.Missing)).Append('\n');
                if (c != null && c.Disallowed.Count > 0)
                    sb.Append("  Not allowed here, turn them off: ").Append(Join(c.Disallowed)).Append('\n');
                if (c != null && c.OptionalNotInstalled.Count > 0)
                    sb.Append("  <color=#A9AFB9>Optional, not installed: ").Append(Join(c.OptionalNotInstalled))
                      .Append("</color>\n");

                AppendOrder(sb, want);
                sb.Append('\n');
            }

            if (!settings && !order)
                sb.Append(KmhText.Get("KMH_Consent_NoChange", "Nothing on this machine has to change.") + "\n\n");

            sb.Append("<b>RimWorld may restart once</b>, and only once, to apply everything above together. "
                    + "Nothing is changed until you choose Apply.\n");

            if (EnforcementProfileApplier.BootFailedFor(EnforcementCache.ServerProfileHash))
                sb.Append("\n<color=#f0c04f>").Append(KmhText.Get("KMH_Consent_BootFailed", "Last time these settings were applied, RimWorld did not reach the main menu, and your own were put back. Applying them again may do the same.")).Append("</color>\n");

            if (EnforcementProfileApplier.RecoveryRequired)
                sb.Append("\n<color=#f0c04f>A previous attempt did not finish cleanly. Colony Nexus will restore your "
                        + "files from its recovery copy before trying again.</color>\n");

            return sb.ToString();
        }

        // Only the mods the server names are compared; a player's own extras are left where they are.
        private static void AppendOrder(System.Text.StringBuilder sb, System.Collections.Generic.List<string> want)
        {
            // What the apply will actually write, worked out the way the apply works it out.
            AppendSwitches(sb, Features.Mods.KmhModPlan.ForOrder(want, Features.Global.KmhLocalMods.Installed()));
            System.Collections.Generic.List<string> mine =
                EnforcementProfileApplier.FilterToNamed(Features.Global.KmhLocalMods.ActivePackageIds(), want);

            if (ModSyncApply.SameSequence(mine, want))
            { sb.Append("  Load order already matches.\n"); return; }

            sb.Append("  Load order will be changed to:\n");
            // The ids stay raw in the cache because they are matched against installed mods; inert where drawn.
            for (int i = 0; i < want.Count; i++)
            {
                bool same = i < mine.Count && string.Equals(mine[i], want[i], StringComparison.OrdinalIgnoreCase);
                string wanted = KmhDisplayText.Inert(want[i], EnforcementCache.MaxModIdChars);
                sb.Append("    ").Append(i + 1).Append(". ")
                  .Append(same ? wanted : "<b>" + wanted + "</b>");
                if (!same && i < mine.Count)
                    sb.Append("   <color=#A9AFB9>(you have ").Append(KmhDisplayText.Inert(mine[i], EnforcementCache.MaxModIdChars))
                      .Append(")</color>");
                sb.Append('\n');
            }
        }

        // Shared with the file review, which repeats it just before anything is written.
        internal static void AppendSwitches(System.Text.StringBuilder sb, Features.Mods.KmhModPlan plan)
        {
            if (plan == null) return;
            if (!plan.Ok)
                sb.Append("  <color=#f0c04f>").Append(KmhText.Get("KMH_Consent_OrderRefused", "This server's mod list will be refused: {0}", KmhDisplayText.Inert(plan.Blocked, 200))).Append("</color>\n");
            else
            {
                if (plan.WillEnable.Count > 0) sb.Append("  ").Append(KmhText.Get("KMH_Consent_WillEnable", "Switched on: {0}", Join(plan.WillEnable))).Append('\n');
                if (plan.WillDisable.Count > 0) sb.Append("  ").Append(KmhText.Get("KMH_Consent_WillDisable", "Switched off: {0}", Join(plan.WillDisable))).Append('\n');
            }
        }

        // Server-supplied names in a dialog the player cannot dismiss, so inert and capped.
        private static string Join(System.Collections.Generic.List<string> items)
        {
            const int Max = 12;
            var shown = new System.Collections.Generic.List<string>(Math.Min(items.Count, Max));
            for (int i = 0; i < items.Count && i < Max; i++) shown.Add(KmhDisplayText.Inert(items[i] ?? "", 64));
            string joined = string.Join(", ", shown.ToArray());
            return items.Count <= Max ? joined : joined + ", and " + (items.Count - Max) + " more";
        }
    }
}
