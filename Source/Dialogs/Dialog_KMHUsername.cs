using KMHPatch.Features.Account;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Changes only the visible handle; the player global id is never touched here.
    public class Dialog_KMHUsername : Window_KMHBase
    {
        protected override bool ClosesOnSessionEnd => false;

        public override Vector2 InitialSize => DialogLayout.FitToScreen(460f, 330f);

        private string _text = "";
        private bool _prefilled;
        private bool _submitted;

        public Dialog_KMHUsername()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public static void Open()
        {
            if (Find.WindowStack.IsOpen<Dialog_KMHUsername>()) return;
            Find.WindowStack.Add(new Dialog_KMHUsername());
        }

        public override void PreOpen()
        {
            base.PreOpen();
            if (KmhUsernameClient.State == UsernameState.Unknown) KmhUsernameClient.Refresh();
        }

        private Vector2 _scroll;
        private float _contentH = 200f, _measuredH = 200f;
        private int _contentFrame = -1;

        protected override void DrawContents(Rect inRect)
        {
            KmhUsernameClient.Tick();
            Prefill();

            // Buttons in a fixed footer, text scrolling above, or at 1280x720 with UI scale they fall off the window.
            const float btnH = 32f;
            Rect body = new Rect(inRect.x, inRect.y, inRect.width, Mathf.Max(DialogLayout.MinBodyHeight, inRect.height - btnH - 10f));
            if (KmhScroll.NewFrame(ref _contentFrame)) _contentH = _measuredH;
            Rect view = new Rect(0f, 0f, body.width - DialogLayout.ScrollbarReserveWidth, Mathf.Max(_contentH, body.height));
            Widgets.BeginScrollView(body, ref _scroll, view);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(view);

            Text.Font = GameFont.Medium;
            listing.Label(KmhUsernameClient.HasUsername ? "Change your Colony Nexus username" : "Choose your Colony Nexus username");
            Text.Font = GameFont.Small;

            listing.Gap(6f);
            listing.Label(KmhText.Get("KMH_Username_YourNameOnKmh", "<color=#9AA4B2>Your name on Colony Nexus Global - unique across every Colony Nexus player, and it travels with your account. Letters, numbers and underscores, 3-24 characters.</color>"));

            listing.Gap(8f);
            Rect field = listing.GetRect(30f);
            string typed = Widgets.TextField(field, _text, KmhUsernameRules.MaxLength);
            if (typed != _text) { _text = typed; KmhUsernameClient.Typed(_text); }

            listing.Gap(4f);
            listing.Label(Feedback());

            if (!KmhUsernameClient.CanChange && KmhUsernameClient.HasUsername)
            {
                listing.Gap(4f);
                listing.Label($"<color=#f0c04f>You can change it again on {KmhUsernameClient.ChangeAllowedText()}.</color>");
            }

            _measuredH = listing.CurHeight + 4f;
            listing.End();
            Widgets.EndScrollView();

            float btnY = inRect.yMax - btnH;
            string cancel = KmhUsernameClient.HasUsername ? "Cancel" : "Not now";
            string save = KmhText.Get("KMH_Username_SaveUsername", "Save username");
            if (Widgets.ButtonText(new Rect(inRect.x, btnY, DialogLayout.ButtonW(cancel, 110f, inRect.width * 0.45f), btnH), cancel)) Close();
            float saveW = DialogLayout.ButtonW(save, 140f, inRect.width * 0.5f);
            if (CanSubmit() && Widgets.ButtonText(new Rect(inRect.xMax - saveW, btnY, saveW, btnH), save))
            {
                _submitted = true;
                KmhUsernameClient.Set(_text);
            }

            // Only a save this window started closes it; a failure keeps the field so the player can correct it.
            if (!_submitted || KmhUsernameClient.Busy) return;
            if (KmhUsernameClient.State != UsernameState.Ready) { _submitted = false; return; }
            _submitted = false;
            Messages.Message($"Colony Nexus username set to {KmhUsernameClient.Username}.",
                             MessageTypeDefOf.TaskCompletion, false);
            Close();
        }

        private void Prefill()
        {
            if (_prefilled || KmhUsernameClient.State != UsernameState.Ready) return;
            _prefilled = true;
            _text = KmhUsernameClient.HasUsername ? KmhUsernameClient.Username : Suggestion();
            KmhUsernameClient.Typed(_text);
        }

        // Global has no name to suggest for a Steam-only account, so fall back to this machine's Steam persona.
        private static string Suggestion()
        {
            if (KmhUsernameClient.Suggestion.Length > 0) return KmhUsernameClient.Suggestion;
            return KmhUsernameRules.SuggestFrom(KmhSteamPersona.Name);
        }

        private bool CanSubmit()
        {
            if (KmhUsernameClient.Busy || KmhUsernameClient.State == UsernameState.Saving) return false;
            if (KmhUsernameClient.HasUsername && !KmhUsernameClient.CanChange
                && !string.Equals(_text.Trim(), KmhUsernameClient.Username,
                                  System.StringComparison.OrdinalIgnoreCase)) return false;
            return KmhUsernameRules.LooksValid(_text) && _text.Trim().Length > 0;
        }

        private string Feedback()
        {
            string wanted = _text.Trim();
            if (KmhUsernameClient.State == UsernameState.Saving) return "<color=#9AA4B2>Saving...</color>";
            if (KmhUsernameClient.State == UsernameState.Failed)
                return $"<color=#f0c04f>{KmhUsernameClient.Explain(KmhUsernameClient.ErrorCode)}</color>";
            if (KmhUsernameClient.State == UsernameState.GlobalUnavailable)
                return "<color=#f0c04f>Colony Nexus Global could not be reached.</color>";
            if (wanted.Length == 0) return "<color=#9AA4B2>Type a username.</color>";

            string local = KmhUsernameRules.Explain(wanted);
            if (local.Length > 0) return $"<color=#f0c04f>{local}</color>";

            if (string.Equals(wanted, KmhUsernameClient.Username, System.StringComparison.OrdinalIgnoreCase))
                return "<color=#9AA4B2>This is your current username.</color>";

            if (!string.Equals(wanted, KmhUsernameClient.Checked, System.StringComparison.OrdinalIgnoreCase))
                return "<color=#9AA4B2>Checking...</color>";
            if (!KmhUsernameClient.CheckedValid)
                return $"<color=#f0c04f>{KmhUsernameClient.CheckedReason}</color>";
            return KmhUsernameClient.CheckedAvailable
                ? $"<color=#4cc38a>{wanted} is available.</color>"
                : $"<color=#f0c04f>{wanted} is already taken.</color>";
        }
    }
}
