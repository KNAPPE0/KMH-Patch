using System;
using System.Collections.Generic;
using KMHPatch.Items;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Open through KmhItemPickerService, or the source reaches here unfiltered by the safety layer.
    public class Dialog_KMHItemPicker : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(640f, 600f);

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

        // Category dropdown, remembered across pickers for the run so a player does not re-pick it every time.
        private static string _lastCategory = "All";
        private string _category = _lastCategory;

        private List<Row> _visible;
        private string _visibleFilter;
        private string _visibleCategory;

        // Throttled because refreshSource rescans colony stock and returns a fresh object on every call.
        private const float SourcePollSeconds = 0.4f;
        private float _nextSourcePoll;

        private void PickedRefresh()
        {
            if (_closeOnPick) { Close(); return; }
            _nextSourcePoll = 0f;
            _visible = null;
        }

        // Compared by contents, since a rebuilt dictionary is a new object on every poll.
        private static bool SameCounts(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (KeyValuePair<string, int> kv in a)
                if (!b.TryGetValue(kv.Key, out int v) || v != kv.Value) return false;
            return true;
        }

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

        internal const float ToolbarGap    = 6f;
        internal const float MinSearchW    = 130f;
        internal const float MinCategoryW  = 90f;

        // The category yields width first, so the search field never drops below readable.
        internal static float CategoryW(float rectWidth, float measured)
            => Mathf.Min(measured, Mathf.Max(MinCategoryW, rectWidth - MinSearchW - ToolbarGap));

        internal static float SearchW(float rectWidth, float categoryW)
            => Mathf.Max(40f, rectWidth - categoryW - ToolbarGap);

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, _title);
            DialogLayout.DrawSectionDivider(rect, ref y);

            // Matched against the resolved label, so "wood" finds defName "WoodLog".
            float catW = CategoryW(rect.width, DialogLayout.DropdownWidth(KmhItemCategories.Dropdown));
            _filter = DialogLayout.SearchField(new Rect(0f, y, SearchW(rect.width, catW), 28f), _filter, "Filter by name");
            if (DialogLayout.DrawDropdownButton(new Rect(rect.width - catW, y, catW, 28f), _category))
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

            Rect listBox = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(listBox);
            DrawList(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void DrawList(Rect box)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 30f;
            const float btnW = 90f;

            // A withdraw's snapshot can land a round-trip AFTER the pick, so one refresh would leave stale counts.
            if (Time.realtimeSinceStartup >= _nextSourcePoll)
            {
                _nextSourcePoll = Time.realtimeSinceStartup + SourcePollSeconds;

                if (_refreshSource != null)
                {
                    var s = _refreshSource();
                    if (s != null && !ReferenceEquals(s, _source) && !SameCounts(s, _source)) { _source = s; _visible = null; }
                    else if (s != null) _source = s;
                }
                if (_refreshPayloads != null)
                {
                    var p = _refreshPayloads();
                    if (p != null && !ReferenceEquals(p, _payloads) && (p.Count != (_payloads?.Count ?? -1))) { _payloads = p; _visible = null; }
                    else if (p != null) _payloads = p;
                }
            }

            string filterLower = (_filter ?? "").Trim().ToLower();

            // Source value: >0 is a real count, 0 excludes the row, and negative means unlimited.
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
            // In-view rows only: a modded catalog can be thousands of items.
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
                    KmhThingPayload pl = r.Payload;
                    KmhItemRow.DrawPayload(row, pl, btnW + KmhItemInfo.Size + 14f);
                    KmhItemInfo.ButtonForDef(viewRect.width - btnW - KmhItemInfo.Size - 10f, ly + 3f, pl.DefName, pl.StuffDefName);
                    if (Widgets.ButtonText(btn, $"{_pickActionLabel}"))
                    {
                        Find.WindowStack.Add(new Dialog_KMHAmountInput(
                            title:        $"{_pickActionLabel} {KmhItemRow.PayloadLabel(pl)}",
                            confirmLabel: _pickActionLabel,
                            unitLabel:    "items",
                            maxHint:      pl.StackCount,
                            onConfirm:    qty => { _onPickPayload?.Invoke(pl, qty); PickedRefresh(); }));
                    }
                    continue;
                }

                Items.KmhItemDecision decision = KmhItemRow.Draw(row, r.Key, r.Count, btnW + KmhItemInfo.Size + 14f);
                string label = ItemLabels.ResolveLabel(r.Key);

                KmhItemInfo.ButtonForKey(viewRect.width - btnW - KmhItemInfo.Size - 10f, ly + 3f, r.Key);

                string capturedDefName = r.Key;
                int    capturedMax     = r.Count > 0 ? r.Count : 0;
                if (!decision.Allowed)
                {
                }
                else if (Widgets.ButtonText(btn, $"{_pickActionLabel}"))
                {
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title:        $"{_pickActionLabel} {label}",
                        confirmLabel: _pickActionLabel,
                        unitLabel:    "units",
                        maxHint:      capturedMax,
                        onConfirm:    qty => { _onPick?.Invoke(capturedDefName, qty); PickedRefresh(); }));
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
