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

        // Cached filtered+sorted view, rebuilt only when the filter changes (the source is fixed at construction).
        private List<KeyValuePair<string, int>> _visible;
        private string _visibleFilter;

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

            // Build the filtered + alphabetically-ordered view ONCE per filter change (not every frame). Filter
            // against the resolved label so 'plasteel knife' matches defName 'Knife' (Stuff=Plasteel). Source value:
            // >0 = real count (shown 'x N'); 0 = excluded; <0 = unlimited (no count, no max in the qty prompt).
            if (_visible == null || _visibleFilter != filterLower)
            {
                _visible = Build(filterLower);
                _visibleFilter = filterLower;
            }
            List<KeyValuePair<string, int>> visible = _visible;

            float viewH    = Mathf.Max(inner.height, visible.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            // Draw only the rows actually in view - a modded catalog can be thousands of items.
            DialogLayout.VisibleRange(_scroll, inner.height, rowH, visible.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                KeyValuePair<string, int> kv = visible[i];
                float ly = i * rowH;
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                // 22px icon column + label + count.
                const float iconSize = 22f;
                ItemLabels.DrawIcon(new Rect(6f, ly + 4f, iconSize, iconSize), kv.Key);

                string label = ItemLabels.ResolveLabel(kv.Key);
                string countText = kv.Value > 0 ? $"<color=grey>x{kv.Value}</color>" : "";
                DialogLayout.LabelTrunc(new Rect(6f + iconSize + 8f, ly + 6f, viewRect.width - btnW - iconSize - 24f, rowH - 12f),
                    $"{label}   {countText}");

                string capturedDefName = kv.Key;
                int    capturedMax     = kv.Value > 0 ? kv.Value : 0;
                Rect   btn             = new Rect(viewRect.width - btnW - 4f, ly + 3f, btnW, rowH - 6f);
                if (Widgets.ButtonText(btn, $"{_pickActionLabel}…"))
                {
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title:        $"{_pickActionLabel} {label}",
                        confirmLabel: _pickActionLabel,
                        unitLabel:    "units",
                        maxHint:      capturedMax,
                        onConfirm:    qty => _onPick?.Invoke(capturedDefName, qty)));
                }
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

        private List<KeyValuePair<string, int>> Build(string filterLower)
        {
            List<KeyValuePair<string, int>> outList = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> kv in _source)
            {
                if (kv.Value == 0) continue;
                if (filterLower.Length > 0)
                {
                    string label = ItemLabels.ResolveLabel(kv.Key).ToLower();
                    if (!label.Contains(filterLower) && !kv.Key.ToLower().Contains(filterLower)) continue;
                }
                outList.Add(kv);
            }
            outList.Sort((a, b) => string.Compare(ItemLabels.ResolveLabel(a.Key), ItemLabels.ResolveLabel(b.Key),
                StringComparison.OrdinalIgnoreCase));
            return outList;
        }
    }
}
