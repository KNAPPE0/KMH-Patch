using System;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Forced choice for server-enforced config changes: apply now or disconnect, with no silent apply or escape-out.
    public class Dialog_KMHEnforcementConsent : Window
    {
        private readonly Action _onClose;
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(520f, 300f);

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
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "Config enforcement");
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(0f, 40f, inRect.width, inRect.height - 40f - 44f),
                "This server enforces mod configs for fair, in-sync play.\n\n" +
                "To play here, your current configs will be backed up, the server's config profile applied, and " +
                "<b>RimWorld will restart</b> so the settings take effect. Otherwise, disconnect.\n\n" +
                "<color=grey>Your personal configs are restored automatically when you leave, or via " +
                "\"Restore my original configs\" in the KMH tab / Mod Options / main menu.</color>");

            float by = inRect.height - 36f;
            float bw = (inRect.width - 12f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, by, bw, 32f), "Apply & restart"))
            {
                EnforcementFlow.ApplyNow();
                Close();
            }
            if (Widgets.ButtonText(new Rect(bw + 12f, by, bw, 32f), "Disconnect"))
            {
                EnforcementFlow.DeclineAndDisconnect();
                Close();
            }
        }
    }
}
