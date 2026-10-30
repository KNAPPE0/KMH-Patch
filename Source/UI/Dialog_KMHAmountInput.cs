using System;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // maxHint is informational only: the server is authoritative, and a client-side cap would only disagree with it.
    public class Dialog_KMHAmountInput : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(380f, 180f);

        private readonly string       _title;
        private readonly string       _confirmLabel;
        private readonly string       _unitLabel;
        private readonly int          _maxHint;
        private readonly Action<int>  _onConfirm;

        private string _input = "";

        public Dialog_KMHAmountInput(
            string title,
            string confirmLabel,
            string unitLabel,
            int    maxHint,
            Action<int> onConfirm,
            string initial = "")
        {
            _title        = title;
            _confirmLabel = confirmLabel;
            _unitLabel    = unitLabel ?? "";
            _maxHint      = maxHint;
            _onConfirm    = onConfirm;
            _input        = initial ?? "";

            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = false;
        }

        protected override void DrawContents(Rect rect)
        {
            // Wraps to a second line: cut to one, "Buy … from <seller>" lost the seller.
            Text.Font = GameFont.Medium;
            float titleH = Text.CalcHeight(_title ?? "", rect.width);
            if (titleH <= Text.LineHeight * 2f + 2f) Widgets.Label(new Rect(0f, 0f, rect.width, titleH), _title);
            else { titleH = Text.LineHeight; DialogLayout.LabelTrunc(new Rect(0f, 0f, rect.width, titleH), _title); }
            Text.Font = GameFont.Small;
            float y = Mathf.Max(28f, titleH) + 4f;

            string hint = HintFor(_maxHint, _unitLabel);
            if (hint.Length > 0)
            {
                Color old = GUI.color;
                GUI.color = DialogLayout.MutedColor;
                y += DialogLayout.LabelWrapped(0f, y, rect.width, hint) + 4f;
                GUI.color = old;
            }

            Rect inputRect = new Rect(0f, y, rect.width, 28f);
            _input = Widgets.TextField(inputRect, _input ?? "");

            const float btnW = 110f;
            const float btnH = 30f;
            GrowToFit(inputRect.yMax + 8f + btnH + 4f, rect.height);
            float btnY = rect.height - btnH - 4f;

            if (Widgets.ButtonText(new Rect(0f, btnY, btnW, btnH), KmhText.Get("KMH_Common_Cancel", "Cancel")))
            {
                Close();
            }
            if (Widgets.ButtonText(new Rect(rect.width - btnW, btnY, btnW, btnH), _confirmLabel))
            {
                Confirm();
            }
        }

        // The unit says what to type ("silver (min 500)"), so it shows with or without a known maximum.
        internal static string HintFor(int maxHint, string unitLabel)
        {
            string unit = (unitLabel ?? "").Trim();
            if (maxHint > 0) return unit.Length == 0 ? KmhText.Get("KMH_Amount_Available", "Available: {0}", maxHint)
                                                     : KmhText.Get("KMH_Amount_AvailableUnit", "Available: {0} {1}", maxHint, unit);
            return unit.Length == 0 ? "" : KmhText.Get("KMH_Amount_In", "Amount in {0}", unit);
        }

        // Grown, never shrunk, so a wrapped title or hint cannot push the field under the buttons and the size settles.
        private void GrowToFit(float need, float have)
        {
            if (need <= have + 1f) return;
            float grow = Mathf.Min(need - have, Mathf.Max(0f, Verse.UI.screenHeight - windowRect.height - 8f));
            if (grow <= 0f) return;
            windowRect.height += grow;
            windowRect.y = Mathf.Clamp(windowRect.y - grow / 2f, 0f, Mathf.Max(0f, Verse.UI.screenHeight - windowRect.height));
        }

        private void Confirm()
        {
            if (!int.TryParse((_input ?? "").Trim(), out int amount) || amount <= 0)
            {
                Notifications.KmhNotifications.Rejected(KmhText.Get("KMH_Amount_BadNumber", "Enter a positive whole number"));
                return;
            }
            try   { _onConfirm?.Invoke(amount); }
            catch { /* handler-side already logs through KmhLog */ }
            Close();
        }
    }
}
