using System;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Reusable single-line text input modal. Counterpart to Dialog_KMHAmountInput for string mutations (MOTDs,
    // guild names, quest titles in the future composer, etc.)
    //
    // Empty-string Confirm is allowed by default - caller decides whether an empty value is meaningful (e.g. "clear
    // MOTD"). Set rejectEmpty to true to enforce non-empty input
    public class Dialog_KMHTextInput : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(460f, 180f);

        private readonly string         _title;
        private readonly string         _confirmLabel;
        private readonly int            _maxChars;
        private readonly bool           _rejectEmpty;
        private readonly Action<string> _onConfirm;

        private string _input;

        public Dialog_KMHTextInput(
            string         title,
            string         confirmLabel,
            string         initial,
            int            maxChars,
            bool           rejectEmpty,
            Action<string> onConfirm)
        {
            _title        = title;
            _confirmLabel = confirmLabel;
            _input        = initial ?? "";
            _maxChars     = maxChars > 0 ? maxChars : 256;
            _rejectEmpty  = rejectEmpty;
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

            Rect inputRect = new Rect(0f, 36f, rect.width, 28f);
            string next = Widgets.TextField(inputRect, _input ?? "");
            if (next != null && next.Length <= _maxChars) _input = next;

            // Char counter
            Color old = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, 66f, rect.width, 18f), $"{(_input ?? "").Length} / {_maxChars}");
            GUI.color = old;

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
            string val = _input ?? "";
            if (_rejectEmpty && string.IsNullOrWhiteSpace(val))
            {
                Notifications.KmhNotifications.Rejected("Value cannot be empty");
                return;
            }
            try   { _onConfirm?.Invoke(val); }
            catch { /* handler logs through KmhLog */ }
            Close();
        }
    }
}
