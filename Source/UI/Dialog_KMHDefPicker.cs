using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Reusable single-select picker over a list of defs (animals, pawn kinds, buildings). Filter by label, click a
    // row to pick (no qty prompt); returns the def's defName (what the server/auto-verify match on) plus its label
    public class Dialog_KMHDefPicker : Window_KMHBase
    {
        public struct Entry
        {
            public string DefName;
            public string Label;
            public Def    Icon;   // optional - drawn via Widgets.DefIcon when set
        }

        public override Vector2 InitialSize => new Vector2(640f, 600f);

        private readonly string                  _title;
        private readonly List<Entry>             _source;
        private readonly Action<string, string>  _onPick;  // (defName, label)

        private Vector2 _scroll;
        private string  _filter = "";

        // Cached filtered+sorted view, rebuilt only when the filter changes (the source is fixed at construction).
        private List<Entry> _visible;
        private string _visibleFilter;

        public Dialog_KMHDefPicker(string title, List<Entry> source, Action<string, string> onPick)
        {
            _title  = title;
            _source = source ?? new List<Entry>();
            _onPick = onPick;

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

            _filter = DialogLayout.SearchField(new Rect(0f, y, rect.width, 28f), _filter, "Filter by name…");
            y += 34f;

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
            if (_visible == null || _visibleFilter != filterLower)
            {
                _visible = Build(filterLower);
                _visibleFilter = filterLower;
            }
            List<Entry> visible = _visible;

            float viewH    = Mathf.Max(inner.height, visible.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            // Draw only on-screen rows - a modded server can expose thousands of defs.
            DialogLayout.VisibleRange(_scroll, inner.height, rowH, visible.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                Entry e = visible[i];
                float ly = i * rowH;
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                const float iconSize = 24f;
                if (e.Icon != null)
                {
                    try { Widgets.DefIcon(new Rect(4f, ly + 3f, iconSize, iconSize), e.Icon); }
                    catch { /* some defs have no drawable icon - skip */ }
                }
                DialogLayout.LabelTrunc(new Rect(4f + iconSize + 8f, ly + 6f, viewRect.width - btnW - iconSize - 24f, rowH - 12f),
                    e.Label ?? e.DefName);

                string capturedDef   = e.DefName;
                string capturedLabel = e.Label ?? e.DefName;
                if (Widgets.ButtonText(new Rect(viewRect.width - btnW - 4f, ly + 3f, btnW, rowH - 6f), "Select"))
                {
                    _onPick?.Invoke(capturedDef, capturedLabel);
                    Close();
                }
            }
            if (visible.Count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(8f, 8f, viewRect.width, 20f),
                    _source.Count == 0 ? "<color=grey>Nothing available.</color>" : "<color=grey>No matches.</color>");
            }
            Widgets.EndScrollView();
        }

        private List<Entry> Build(string filterLower)
        {
            List<Entry> outList = new List<Entry>();
            foreach (Entry e in _source)
            {
                if (filterLower.Length > 0
                    && !(e.Label ?? "").ToLower().Contains(filterLower)
                    && !(e.DefName ?? "").ToLower().Contains(filterLower)) continue;
                outList.Add(e);
            }
            outList.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            return outList;
        }
    }
}
