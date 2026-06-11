using System;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Reusable single-positive-integer input modal. Cancel + Confirm.
    //
    // Used by every KMH mutation that needs the player to enter a count - Treasury deposit/withdraw silver,
    // Marketplace buy qty, future perk-level / tax-percent / withdraw-cap settings dialogs, etc
    //
    // maxHint is purely informational ("Available: N {unitLabel}" subline). 0 = no hint shown. We don't enforce it
    // client-side - server is authoritative for actual balance/permission/quantity validation
    public class Dialog_KMHAmountInput : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(380f, 180f);

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
            Action<int> onConfirm)
        {
            _title        = title;
            _confirmLabel = confirmLabel;
            _unitLabel    = unitLabel ?? "";
            _maxHint      = maxHint;
            _onConfirm    = onConfirm;

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
