using System;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    public class Dialog_KMHMultilineTextInput : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(620f, 420f);

        private readonly string         _title;
        private readonly string         _confirmLabel;
        private readonly int            _maxChars;
        private readonly bool           _rejectEmpty;
        private readonly Action<string> _onConfirm;

        private string  _input;
        private Vector2 _scroll;

        public Dialog_KMHMultilineTextInput(
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
            _maxChars     = maxChars > 0 ? maxChars : 2048;
            _rejectEmpty  = rejectEmpty;
            _onConfirm    = onConfirm;

            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;
        }

        protected override void DrawContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            DialogLayout.LabelTrunc(new Rect(0f, 0f, rect.width, 28f), _title);
            Text.Font = GameFont.Small;

            const float btnH        = 32f;
            const float btnRowGap   = 8f;
            const float counterH    = 18f;
            const float headerH     = 36f;
            float editorH = rect.height - headerH - counterH - btnH - btnRowGap * 2f;

            // Verse.Widgets has no TextAreaScrollable in 1.6, so this draws its own scroll view.
            Rect editorRect = new Rect(0f, headerH, rect.width, editorH);
            float lineH = Text.LineHeight;
            // TextArea draws into whatever rect it gets, so the viewRect is measured to lead its growth.
            float editW = Mathf.Max(1f, editorRect.width - 18f);
            float textH = Text.CalcHeight(string.IsNullOrEmpty(_input) ? " " : _input, editW);
            Rect viewRect = new Rect(0f, 0f, editW, Mathf.Max(textH + lineH * 2f, editorH));
            Widgets.BeginScrollView(editorRect, ref _scroll, viewRect);
            string next = Widgets.TextArea(viewRect, _input ?? "");
            Widgets.EndScrollView();
            if (next != null)
            {
                // Trimmed on the way in, so a paste cannot exceed the cap.
                _input = next.Length > _maxChars ? next.Substring(0, _maxChars) : next;
            }

            Color old = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, headerH + editorH + 2f, rect.width, counterH),
                $"{(_input ?? "").Length} / {_maxChars}");
            GUI.color = old;

            const float btnW = 130f;
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
            if (Dialog_KMHTextInput.TryConfirm(_input, _rejectEmpty, _onConfirm)) Close();
        }
    }
}
