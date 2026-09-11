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
            string initial = "")   // pre-fills the field (e.g. an auction's minimum bid)
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
            Text.Font = GameFont.Medium;
            DialogLayout.LabelTrunc(new Rect(0f, 0f, rect.width, 28f), _title);
            Text.Font = GameFont.Small;

            if (_maxHint > 0)
            {
                Color old = GUI.color;
                GUI.color = DialogLayout.MutedColor;
                string unit = string.IsNullOrEmpty(_unitLabel) ? "" : " " + _unitLabel;
                DialogLayout.LabelTrunc(new Rect(0f, 30f, rect.width, 18f), $"Available: {_maxHint}{unit}");
                GUI.color = old;
            }

            Rect inputRect = new Rect(0f, 56f, rect.width, 28f);
            _input = Widgets.TextField(inputRect, _input ?? "");

            const float btnW = 110f;
            const float btnH = 30f;
            float btnY = rect.height - btnH - 4f;

            if (Widgets.ButtonText(new Rect(0f, btnY, btnW, btnH), "Cancel"))
            {
                Close();
            }
            if (Widgets.ButtonText(new Rect(rect.width - btnW, btnY, btnW, btnH), _confirmLabel))
            {
                Confirm();
            }
        }

        private void Confirm()
        {
            if (!int.TryParse((_input ?? "").Trim(), out int amount) || amount <= 0)
            {
                Notifications.KmhNotifications.Rejected("Enter a positive whole number");
                return;
            }
            try   { _onConfirm?.Invoke(amount); }
            catch { /* handler-side already logs through KmhLog */ }
            Close();
        }
    }
}
