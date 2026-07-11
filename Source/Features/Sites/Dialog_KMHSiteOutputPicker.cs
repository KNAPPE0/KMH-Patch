using System;
using System.Collections.Generic;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Curated Site output picker. Shows ONLY what the server says a site may produce (the server-classified catalog),
    // grouped by tier with skill / max amount / estimated cost + cycle. It is deliberately NOT the generic all-def
    // browser - searching for weapons/apparel/tech/genes/etc. turns up nothing unless an admin enables the debug view.
    public class Dialog_KMHSiteOutputPicker : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(760f, 620f);

        private readonly Action<SiteCatalogEntry> _onPick;
        private Vector2 _scroll;
        private string  _filter = "";
        private int     _tierFilter;      // 0 = all, 1..4 = that tier
        private bool    _showBlocked;     // debug: also show blocked items + reasons
        private float   _refresh;
        private static string _lastCategory = "All";
        private string  _category = _lastCategory;   // works alongside the tier filter (both AND'd)

        public Dialog_KMHSiteOutputPicker(Action<SiteCatalogEntry> onPick)
        {
            _onPick = onPick;
            doCloseX = true; absorbInputAroundWindow = true; draggable = true; resizeable = true;
            // Seed the server catalog fast on a fresh server (idempotent), then request the classified catalog.
            try { Features.Catalog.ItemLabelsSender.PushOnce(); } catch { }
            SiteHandler.RequestCatalog(_showBlocked);
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Choose a site output");
            DialogLayout.DrawSectionDivider(rect, ref y);

            SiteCatalogSnapshot cat = SiteCatalogCache.Catalog;

            // Tier filter tabs.
            const float tabW = 92f;
            string[] tabs = { "All", "T1 Basic", "T2 Refined", "T3 Advanced", "T4 Rare/Tech" };
            for (int i = 0; i < tabs.Length; i++)
            {
                Rect tb = new Rect(i * (tabW + 4f), y, tabW, 26f);
                bool on = _tierFilter == i;
                if (on) Widgets.DrawHighlightSelected(tb);
                if (Widgets.ButtonText(tb, tabs[i])) _tierFilter = i;
            }
            y += 32f;

            _filter = DialogLayout.SearchField(new Rect(0f, y, 290f, 28f), _filter, "Search outputs…");
            if (Widgets.ButtonText(new Rect(296f, y, 196f, 28f), $"Category: {_category}"))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                foreach (string c in KmhItemCategories.Dropdown)
                { string cap = c; opts.Add(new FloatMenuOption(cap, () => { _category = cap; _lastCategory = cap; })); }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            bool prev = _showBlocked;
            Widgets.CheckboxLabeled(new Rect(rect.width - 210f, y + 2f, 210f, 24f), "Show blocked (admin)", ref _showBlocked);
            if (_showBlocked != prev) SiteHandler.RequestCatalog(_showBlocked);   // re-fetch with/without blocked
            y += 34f;

            if (cat != null && cat.CatalogSparse)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                    "<color=#E2C16B>Catalog still loading on this server - showing common resources. Reconnect or wait a few seconds for the full list.</color>");
                y += 22f;
            }

            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawList(listBox, cat);

            if (DialogLayout.DrawCloseButton(rect)) Close();
            AutoRefresh();
        }

        private void DrawList(Rect box, SiteCatalogSnapshot cat)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);

            if (cat == null)
            {
                DialogLayout.LabelTrunc(new Rect(inner.x + 4f, inner.y + 4f, inner.width, 22f),
                    "<color=grey>Loading site outputs…</color>");
                return;
            }
            if (_tierFilter == 4 && !cat.Tier4Enabled && !_showBlocked)
            {
                DialogLayout.LabelTrunc(new Rect(inner.x + 4f, inner.y + 4f, inner.width - 8f, 40f),
                    "<color=grey>Tier 4 (crafted / rare / tech) outputs are disabled on this server - a site can't print gear, tech, drugs, genes, or relics.</color>");
                return;
            }

            List<SiteCatalogEntry> rows = Filtered(cat);
            const float rowH = 34f;
            float viewH = Mathf.Max(inner.height, rows.Count * rowH + 8f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            DialogLayout.VisibleRange(_scroll, inner.height, rowH, rows.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                SiteCatalogEntry e = rows[i];
                float ly = i * rowH;
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                const float iconSize = 24f;
                ItemLabels.DrawIcon(new Rect(6f, ly + 5f, iconSize, iconSize), e.DefName);

                Color old = GUI.color;
                if (!e.Allowed) GUI.color = new Color(0.75f, 0.55f, 0.55f);
                // Line 1: label + tier badge + skill.
                DialogLayout.LabelTrunc(new Rect(38f, ly + 2f, viewRect.width - 150f, 18f),
                    $"<b>{e.Label}</b>  <color=grey>· T{e.Tier} {e.TierName} · {e.RelevantSkill}</color>");
                // Line 2: max / cost / cycle, or the block reason.
                DialogLayout.LabelTrunc(new Rect(38f, ly + 18f, viewRect.width - 150f, 16f),
                    e.Allowed
                        ? $"<color=grey>max {e.MaxAmount}/cycle · ~{SilverFmt.Format(e.EstBuildCost)} build · ~{e.EstCycleMinutes} min/cycle</color>"
                        : $"<color=#ff8080>blocked: {e.BlockReason}</color>");
                GUI.color = old;

                Rect btn = new Rect(viewRect.width - 96f, ly + 5f, 90f, rowH - 10f);
                if (e.Allowed)
                {
                    if (Widgets.ButtonText(btn, "Select")) { _onPick?.Invoke(e); Close(); }
                }
                else
                {
                    DialogLayout.LabelTrunc(new Rect(viewRect.width - 96f, ly + 9f, 90f, 18f), "<color=grey>blocked</color>");
                }
            }
            if (rows.Count == 0)
                DialogLayout.LabelTrunc(new Rect(8f, 8f, viewRect.width, 20f),
                    "<color=grey>No matching outputs. Sites only produce curated resources - not weapons, apparel, tech, or rare items.</color>");
            Widgets.EndScrollView();
        }

        private List<SiteCatalogEntry> Filtered(SiteCatalogSnapshot cat)
        {
            string f = (_filter ?? "").Trim().ToLowerInvariant();
            List<SiteCatalogEntry> outList = new List<SiteCatalogEntry>();
            foreach (SiteCatalogEntry e in cat.Entries)
            {
                if (e == null) continue;
                if (!e.Allowed && !_showBlocked) continue;
                if (_tierFilter > 0 && e.Tier != _tierFilter) continue;
                if (!KmhItemCategories.Matches(e.DefName, _category)) continue;   // category AND tier both apply
                if (f.Length > 0 && (e.Label ?? "").ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0
                                 && (e.DefName ?? "").ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0) continue;
                outList.Add(e);
            }
            return outList;
        }

        private void AutoRefresh()
        {
            _refresh -= Time.deltaTime;
            if (_refresh <= 0f) { _refresh = 6f; SiteHandler.RequestCatalog(_showBlocked); }
        }
    }
}
