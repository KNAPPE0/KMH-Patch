using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Reusable two-step item picker:
    //   1. Show available items + per-row "Pick…" buttons.
    //   2. Picking opens Dialog_KMHAmountInput with maxHint = available count.
    //   3. Confirm invokes the onPick callback with (defName, qty).
    //
    // Source is a defName -> count dict. Caller's responsibility to populate it from the right place (caravan
    // items, treasury items, etc.); this dialog doesn't know or care
    //
    // Used by: Treasury Deposit (source=caravan), Treasury Withdraw (source=treasury), Marketplace Post
    // (source=caravan, future commit)
    public class Dialog_KMHItemPicker : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(640f, 600f);

        private readonly string                        _title;
        private readonly string                        _pickActionLabel;   // e.g. "Deposit" / "Withdraw" / "List"
        private readonly Dictionary<string, int>       _source;
        private readonly Action<string, int>           _onPick;

        private Vector2 _scroll;
        private string  _filter = "";

        public Dialog_KMHItemPicker(
            string                        title,
            string                        pickActionLabel,
            Dictionary<string, int>       source,
            Action<string, int>           onPick)
        {
            _title           = title;
            _pickActionLabel = pickActionLabel;
            _source          = source ?? new Dictionary<string, int>();
            _onPick          = onPick;

            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, _title);
            DialogLayout.DrawSectionDivider(rect, ref y);

            // Filter row - simple text contains-match against the resolved human label so players can type 'wood'
            // even when the defName is 'WoodLog'
            _filter = DialogLayout.SearchField(new Rect(0f, y, rect.width, 28f), _filter, "Filter by name…");
            y += 34f;

            // Item list
            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawList(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void DrawList(Rect box)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 30f;
            const float btnW = 90f;

            string filterLower = (_filter ?? "").Trim().ToLower();

            // Filtered + alphabetically-ordered view. Filter against the resolved label rather than defName so
            // 'plasteel knife' matches a defName like 'Knife' (with Stuff=Plasteel) even though the stack key is
            // just the item defName
            //
            // Source value semantics: > 0 real available count (shown 'x N', max-hint enforced in the qty prompt) =
            // 0 item excluded entirely (zero stock) < 0 "unlimited" - no count shown, no max in the qty prompt.
            // Used by ItemDefBrowser-style sources (Quest target picker, etc.) where the player picks an item def
            // and any positive qty is fine
            List<KeyValuePair<string, int>> visible = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> kv in _source)
            {
                if (kv.Value == 0) continue;
                if (filterLower.Length > 0)
                {
                    string label = ItemLabels.ResolveLabel(kv.Key).ToLower();
                    if (!label.Contains(filterLower) && !kv.Key.ToLower().Contains(filterLower))
                        continue;
                }
                visible.Add(kv);
            }
            visible.Sort((a, b) =>
                string.Compare(ItemLabels.ResolveLabel(a.Key), ItemLabels.ResolveLabel(b.Key),
                    StringComparison.OrdinalIgnoreCase));

            float viewH    = Mathf.Max(inner.height, visible.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float ly = 0f;

            for (int i = 0; i < visible.Count; i++)
            {
                KeyValuePair<string, int> kv = visible[i];
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                // 22px icon column + label + count.
                const float iconSize = 22f;
                ItemLabels.DrawIcon(new Rect(6f, ly + 4f, iconSize, iconSize), kv.Key);

                string label = ItemLabels.ResolveLabel(kv.Key);
                // Count text: show 'x N' for finite stock, nothing for unlimited entries (value < 0)
                string countText = kv.Value > 0 ? $"<color=grey>x{kv.Value}</color>" : "";
                DialogLayout.LabelTrunc(new Rect(6f + iconSize + 8f, ly + 6f, viewRect.width - btnW - iconSize - 24f, rowH - 12f),
                    $"{label}   {countText}");

                // capture loop locals before the lambda
                string capturedDefName  = kv.Key;
                // Map source semantics to maxHint: unlimited entries pass 0 (Dialog_KMHAmountInput hides the
                // 'Available' line on 0)
                int    capturedMax      = kv.Value > 0 ? kv.Value : 0;
                Rect   btn              = new Rect(viewRect.width - btnW - 4f, ly + 3f, btnW, rowH - 6f);

                if (Widgets.ButtonText(btn, $"{_pickActionLabel}…"))
                {
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title:        $"{_pickActionLabel} {label}",
                        confirmLabel: _pickActionLabel,
                        unitLabel:    "units",
                        maxHint:      capturedMax,
                        onConfirm:    qty => _onPick?.Invoke(capturedDefName, qty)));
                }

                ly += rowH;
            }

            if (visible.Count == 0)
            {
                string emptyMsg = _source.Count == 0
                    ? "<color=grey>No items available.</color>"
                    : "<color=grey>No items match the filter.</color>";
                DialogLayout.LabelTrunc(new Rect(8f, 8f, viewRect.width, 20f), emptyMsg);
            }

            Widgets.EndScrollView();
        }
    }
}
