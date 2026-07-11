using System;
using System.Collections.Generic;
using KMHPatch.Items;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Reusable two-step item picker (pick row -> amount prompt -> onPick callback). Source is a caller-supplied
    // defName->count dict, optionally joined by full-state payload stacks (weapons/apparel with quality/hp) that
    // withdraw by exact state via onPickPayload. Go through KmhItemPickerService so the source is safety-filtered.
    public class Dialog_KMHItemPicker : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(640f, 600f);

        private readonly string                        _title;
        private readonly string                        _pickActionLabel;   // e.g. "Deposit" / "Withdraw" / "List"
        private Dictionary<string, int>                _source;
        private readonly Action<string, int>           _onPick;
        private readonly Func<Dictionary<string, int>> _refreshSource;     // re-read live counts after each pick (optional)

        private IList<KmhThingPayload>                 _payloads;          // full-state stacks shown alongside (optional)
        private readonly Action<KmhThingPayload, int>  _onPickPayload;
        private readonly Func<IList<KmhThingPayload>>  _refreshPayloads;
        private readonly bool                          _closeOnPick;       // composers pick one item then close; vaults stay open for repeats

        private Vector2 _scroll;
        private string  _filter = "";
        private bool    _needsRefresh;

        // Category dropdown, remembered across pickers this session so a player doesn't re-pick it every time.
        private static string _lastCategory = "All";
        private string _category = _lastCategory;

        // Cached filtered+sorted view, rebuilt when the filter/category changes or the source is refreshed after a pick.
        private List<Row> _visible;
        private string _visibleFilter;
        private string _visibleCategory;

        // One list row: either a plain def entry (Key/Count) or a full-state payload stack.
        private struct Row
        {
            public string Key;
            public int Count;
            public KmhThingPayload Payload;
        }

        public Dialog_KMHItemPicker(
            string                        title,
            string                        pickActionLabel,
            Dictionary<string, int>       source,
            Action<string, int>           onPick,
            Func<Dictionary<string, int>> refreshSource = null,
            IList<KmhThingPayload>        payloads = null,
            Action<KmhThingPayload, int>  onPickPayload = null,
            Func<IList<KmhThingPayload>>  refreshPayloads = null,
            bool                          closeOnPick = false)
        {
            _title           = title;
            _pickActionLabel = pickActionLabel;
            _source          = source ?? new Dictionary<string, int>();
            _onPick          = onPick;
            _refreshSource   = refreshSource;
            _payloads        = payloads;
            _onPickPayload   = onPickPayload;
            _refreshPayloads = refreshPayloads;
            _closeOnPick     = closeOnPick;

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
            float catW = 210f;
            _filter = DialogLayout.SearchField(new Rect(0f, y, rect.width - catW - 6f, 28f), _filter, "Filter by name…");
            if (Widgets.ButtonText(new Rect(rect.width - catW, y, catW, 28f), $"Category: {_category}"))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                foreach (string cat in KmhItemCategories.Dropdown)
                {
                    string captured = cat;
                    opts.Add(new FloatMenuOption(captured, () => { _category = captured; _lastCategory = captured; }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
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

            // After a pick, re-read live counts so the list can't show stock the colony no longer holds (which would
            // let a second attempt fail with "only has 0" while the first success toast is still on screen).
            if (_needsRefresh)
            {
                _needsRefresh = false;
                if (_refreshSource != null)   { _source   = _refreshSource()   ?? _source;   _visible = null; }
                if (_refreshPayloads != null) { _payloads = _refreshPayloads() ?? _payloads; _visible = null; }
            }

            string filterLower = (_filter ?? "").Trim().ToLower();

            // Build the filtered + alphabetically-ordered view ONCE per filter change (not every frame). Filter
            // against the resolved label so 'plasteel knife' matches defName 'Knife' (Stuff=Plasteel). Source value:
            // >0 = real count (shown 'x N'); 0 = excluded; <0 = unlimited (no count, no max in the qty prompt).
            if (_visible == null || _visibleFilter != filterLower || _visibleCategory != _category)
            {
                _visible = Build(filterLower);
                _visibleFilter = filterLower;
                _visibleCategory = _category;
            }
            List<Row> visible = _visible;

            float viewH    = Mathf.Max(inner.height, visible.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            // Draw only the rows actually in view - a modded catalog can be thousands of items.
            DialogLayout.VisibleRange(_scroll, inner.height, rowH, visible.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                Row r = visible[i];
                float ly = i * rowH;
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                Rect btn = new Rect(viewRect.width - btnW - 4f, ly + 3f, btnW, rowH - 6f);

                if (r.Payload != null)
                {
                    // Full-state stack: rich label + state suffix, info card with the exact def+stuff to double-check.
                    KmhThingPayload pl = r.Payload;
                    KmhItemRow.DrawPayload(row, pl, btnW + KmhItemInfo.Size + 14f);
                    KmhItemInfo.ButtonForDef(viewRect.width - btnW - KmhItemInfo.Size - 10f, ly + 3f, pl.DefName, pl.StuffDefName);
                    if (Widgets.ButtonText(btn, $"{_pickActionLabel}…"))
                    {
                        Find.WindowStack.Add(new Dialog_KMHAmountInput(
                            title:        $"{_pickActionLabel} {KmhItemRow.PayloadLabel(pl)}",
                            confirmLabel: _pickActionLabel,
                            unitLabel:    "items",
                            maxHint:      pl.StackCount,
                            onConfirm:    qty => { _onPickPayload?.Invoke(pl, qty); _needsRefresh = true; if (_closeOnPick) Close(); }));
                    }
                    continue;
                }

                // Shared row: icon + label + count, greyed with a reason tooltip when the safety layer blocks the def.
                Items.KmhItemDecision decision = KmhItemRow.Draw(row, r.Key, r.Count, btnW + KmhItemInfo.Size + 14f);
                string label = ItemLabels.ResolveLabel(r.Key);

                // RimWorld's real info card, so the item can be inspected before picking it.
                KmhItemInfo.ButtonForKey(viewRect.width - btnW - KmhItemInfo.Size - 10f, ly + 3f, r.Key);

                string capturedDefName = r.Key;
                int    capturedMax     = r.Count > 0 ? r.Count : 0;
                if (!decision.Allowed)
                {
                    // Blocked def: no action button - the greyed row + tooltip already explains why.
                }
                else if (Widgets.ButtonText(btn, $"{_pickActionLabel}…"))
                {
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title:        $"{_pickActionLabel} {label}",
                        confirmLabel: _pickActionLabel,
                        unitLabel:    "units",
                        maxHint:      capturedMax,
                        onConfirm:    qty => { _onPick?.Invoke(capturedDefName, qty); _needsRefresh = true; if (_closeOnPick) Close(); }));
                }
            }

            if (visible.Count == 0)
            {
                string emptyMsg = _source.Count == 0 && (_payloads == null || _payloads.Count == 0)
                    ? "<color=grey>No items available.</color>"
                    : "<color=grey>No items match the filter.</color>";
                DialogLayout.LabelTrunc(new Rect(8f, 8f, viewRect.width, 20f), emptyMsg);
            }

            Widgets.EndScrollView();
        }

        private List<Row> Build(string filterLower)
        {
            List<Row> outList = new List<Row>();
            foreach (KeyValuePair<string, int> kv in _source)
            {
                if (kv.Value == 0) continue;
                if (!KmhItemCategories.Matches(kv.Key, _category)) continue;
                if (filterLower.Length > 0)
                {
                    string label = ItemLabels.ResolveLabel(kv.Key).ToLower();
                    if (!label.Contains(filterLower) && !kv.Key.ToLower().Contains(filterLower)) continue;
                }
                outList.Add(new Row { Key = kv.Key, Count = kv.Value });
            }
            if (_payloads != null)
                foreach (KmhThingPayload p in _payloads)
                {
                    if (p == null || p.StackCount <= 0) continue;
                    if (!KmhItemCategories.Matches(p.DefName, _category)) continue;
                    if (filterLower.Length > 0)
                    {
                        string hay = (KmhItemRow.PayloadLabel(p) + " " + p.DefName).ToLower();
                        if (!hay.Contains(filterLower)) continue;
                    }
                    outList.Add(new Row { Payload = p });
                }
            outList.Sort((a, b) => string.Compare(LabelOf(a), LabelOf(b), StringComparison.OrdinalIgnoreCase));
            return outList;
        }

        private static string LabelOf(Row r)
            => r.Payload != null ? KmhItemRow.PayloadLabel(r.Payload) : ItemLabels.ResolveLabel(r.Key);
    }
}
