using System;
using System.Collections.Generic;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Deliberately the server's curated catalog, not the all-def browser: weapons/apparel/tech never appear here.
    public class Dialog_KMHSiteOutputPicker : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(760f, 620f);

        private readonly Action<SiteCatalogEntry> _onPick;
        // Ineligible outputs are hidden for a preset, shown disabled for Custom.
        private readonly string _archetype;
        private Vector2 _scroll;
        private string  _filter = "";
        private int     _tierFilter;      // 0 = all, 1..4 = that tier
        private bool    _showBlocked;     // debug: also show blocked items + reasons
        private static string _lastCategory = "All";
        private string  _category = _lastCategory;   // works alongside the tier filter (both AND'd)

        public Dialog_KMHSiteOutputPicker(Action<SiteCatalogEntry> onPick, string archetype = "")
        {
            _onPick = onPick;
            _archetype = (archetype ?? "").Trim();
            doCloseX = true; absorbInputAroundWindow = true; draggable = true; resizeable = true;
            // Once, never on a timer: the catalog is ~120KB and the server broadcasts it whenever it changes.
            try { Features.Catalog.ItemLabelsSender.PushOnce(); } catch { }
            SiteHandler.RequestCatalog(_showBlocked);
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Choose a site output");
            DialogLayout.DrawSectionDivider(rect, ref y);

            SiteCatalogSnapshot cat = SiteCatalogCache.Catalog;

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

            float catW      = DialogLayout.DropdownWidth(KmhItemCategories.Dropdown);
            float blockedW  = DialogLayout.TightCheckboxWidth("Show blocked (admin)");
            float searchW   = Mathf.Clamp(rect.width - catW - blockedW - 20f, 110f, 290f);

            _filter = DialogLayout.SearchField(new Rect(0f, y, searchW, 28f), _filter, "Search outputs");
            if (DialogLayout.DrawDropdownButton(new Rect(searchW + 6f, y, catW, 28f), _category))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                foreach (string c in KmhItemCategories.Dropdown)
                { string cap = c; opts.Add(new FloatMenuOption(cap, () => { _category = cap; _lastCategory = cap; })); }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            bool prev = _showBlocked;
            DialogLayout.DrawTightCheckbox(Mathf.Max(searchW + catW + 12f, rect.width - blockedW), y + 4f,
                                           "Show blocked (admin)", ref _showBlocked);
            if (_showBlocked != prev) SiteHandler.RequestCatalog(_showBlocked);
            y += DialogLayout.ToolbarRowH + 2f;

            // Gated on ArchetypesReady: before classification lands every item would look ineligible, i.e. broken.
            if (cat != null && cat.ArchetypesReady && !string.IsNullOrEmpty(_archetype))
            {
                SiteArchetypeInfo info = FindArchetype(cat, _archetype);
                if (info != null)
                {
                    DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                        $"<color=grey>Showing what a <b>{info.DisplayName}</b> can produce"
                        + (info.IsCustom ? " - anything KMH has classified." : $" · workers use {info.WorkerSkill}.")
                        + "</color>");
                    y += 22f;
                }
            }

            if (cat != null && cat.CatalogSparse)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                    "<color=#E2C16B>Catalog still loading on this server - showing common resources. Reconnect or wait a few seconds for the full list.</color>");
                y += 22f;
            }

            Rect listBox = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(listBox);
            DrawList(listBox, cat);

            if (DialogLayout.DrawCloseButton(rect)) Close();
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

                bool eligible = Eligible(cat, e);
                Color old = GUI.color;
                if (!e.Allowed || !eligible) GUI.color = new Color(0.75f, 0.55f, 0.55f);
                // Line 1: label + tier badge + skill.
                DialogLayout.LabelTrunc(new Rect(38f, ly + 2f, viewRect.width - 150f, 18f),
                    $"<b>{e.Label}</b>  <color=grey>· T{e.Tier} {e.TierName} · {e.RelevantSkill}</color>");
                // Line 2: max / cost / cycle, or the block reason.
                DialogLayout.LabelTrunc(new Rect(38f, ly + 18f, viewRect.width - 150f, 16f),
                    !e.Allowed
                        ? $"<color=#ff8080>blocked: {e.BlockReason}</color>"
                        : eligible
                            ? $"<color=grey>max {e.MaxAmount}/cycle · ~{SilverFmt.Format(e.EstBuildCost)} build · ~{e.EstCycleMinutes} min/cycle</color>"
                            : $"<color=#ff8080>{IneligibleReason(e)}</color>");
                GUI.color = old;

                Rect btn = new Rect(viewRect.width - 96f, ly + 5f, 90f, rowH - 10f);
                if (e.Allowed && eligible)
                {
                    if (Widgets.ButtonText(btn, "Select")) { _onPick?.Invoke(e); Close(); }
                }
                else
                {
                    DialogLayout.LabelTrunc(new Rect(viewRect.width - 96f, ly + 9f, 90f, 18f),
                        e.Allowed ? "<color=grey>n/a</color>" : "<color=grey>blocked</color>");
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
                // Unknown items must stay visible under Custom, or an owner never learns a family override is needed.
                if (!Eligible(cat, e) && !ShowsIneligible(cat) && !_showBlocked) continue;
                if (_tierFilter > 0 && e.Tier != _tierFilter) continue;
                if (!KmhItemCategories.Matches(e.DefName, _category)) continue;   // category AND tier both apply
                if (f.Length > 0 && (e.Label ?? "").ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0
                                 && (e.DefName ?? "").ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0) continue;
                outList.Add(e);
            }
            return outList;
        }

        // Reads the server's shipped per-item answer; a client-side family test would drift from what Build enforces.
        private bool Eligible(SiteCatalogSnapshot cat, SiteCatalogEntry e)
        {
            if (cat == null || !cat.ArchetypesReady || string.IsNullOrEmpty(_archetype)) return true;
            if (e == null) return false;
            string list = e.AllowedArchetypes ?? "";
            if (list.Length == 0) return false;
            foreach (string id in list.Split(','))
                if (string.Equals(id.Trim(), _archetype, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Custom lists ineligible items (disabled, with a reason); a preset simply omits them.
        private bool ShowsIneligible(SiteCatalogSnapshot cat)
        {
            SiteArchetypeInfo info = FindArchetype(cat, _archetype);
            return info != null && info.IsCustom;
        }

        private string IneligibleReason(SiteCatalogEntry e)
            => string.Equals(e.Family, "unknown", StringComparison.OrdinalIgnoreCase)
             ? "KMH has not classified this item - ask the server owner to set its family"
             : "this site type cannot produce it";

        private static SiteArchetypeInfo FindArchetype(SiteCatalogSnapshot cat, string id)
        {
            if (cat?.Archetypes == null || string.IsNullOrEmpty(id)) return null;
            foreach (SiteArchetypeInfo a in cat.Archetypes)
                if (a != null && string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

    }
}
