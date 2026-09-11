using System;
using System.Collections.Generic;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Marketplace.Dto;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Marketplace
{
    // Marketplace browse: per-listing rows with ownership-gated Buy…/Cancel, newest-first.
    public class Dialog_KMHMarketplace : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(1040f, 620f);

        private Vector2  _scroll;

        // Categories are ThingCategoryDefs, so the filter works for any expansion or mod that files defs under them.
        private string _filter         = "";
        private string _categoryFilter = "All";
        private bool   _onlyMine       = false;
        private bool   _onlyMyGuild    = false;

        private enum SortMode { Newest, PriceAsc, PriceDesc, QtyDesc, NameAsc }
        private SortMode _sort = SortMode.Newest;

        private static string SortLabel(SortMode m)
        {
            switch (m)
            {
                case SortMode.PriceAsc:  return "Price ↑";
                case SortMode.PriceDesc: return "Price ↓";
                case SortMode.QtyDesc:   return "Qty ↓";
                case SortMode.NameAsc:   return "Name A-Z";
                default:                 return "Newest";
            }
        }

        private static readonly string[] CommonCategories =
        {
            "All", "Resources", "Manufactured", "Foods", "Drugs",
            "Medicine", "Weapons", "Apparel", "Plants", "BodyParts", "Other"
        };

        // The stored values are ThingCategoryDef defNames, which the filter matches on; only the display differs.
        private static string CategoryName(string cat)
            => cat == "All" ? "All categories" : cat == "BodyParts" ? "Body parts" : cat;

        private static IEnumerable<string> CategoryNames()
        {
            foreach (string c in CommonCategories) yield return CategoryName(c);
        }

        private List<MarketplaceListing> _visible;
        private object                   _visibleSource;
        private string                   _visibleFilter;
        private string                   _visibleCategory;
        private bool                     _vcMine, _vcMyGuild;
        private SortMode                 _vcSort;

        public Dialog_KMHMarketplace()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            EnableAutoRefresh(() => MarketplaceHandler.RequestSnapshot());
            MarketplaceCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            MarketplaceCache.Updated -= OnSnapshotUpdated;
        }

        // Also invalidate the cached filtered view so the fresh snapshot rebuilds it.
        private void OnSnapshotUpdated()
        {
            _visible = null;
            MarkRefreshed();
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Player Marketplace");
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (!MarketplaceCache.HasSnapshot)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 20f),
                    "<color=grey>Loading marketplace…</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            MarketplaceSnapshot s = MarketplaceCache.Snapshot;

            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                $"House pool: {SilverFmt.Format(s.HouseSilverPool)}  |  " +
                $"Lifetime trades: {s.LifetimeTradesCompleted}  |  " +
                $"Lifetime silver traded: {SilverFmt.Format(s.LifetimeSilverTraded)}");
            GUI.color = oldCol;
            y += 24f;

            // Measured, never fixed to one resolution, and a row each so the pinned buttons cannot slide under the search field.
            float catW = DialogLayout.DropdownWidth(CategoryNames(), 110f, 200f);

            float mineW  = DialogLayout.TightCheckboxWidth("Only mine");
            float guildW = DialogLayout.TightCheckboxWidth("My guild only");
            float sortW  = Mathf.Clamp(Text.CalcSize($"Sort: {SortLabel(SortMode.NameAsc)}").x + 26f, 110f, 170f);

            // The toggles and sort take a row of their own before the search field is squeezed below readability.
            const float searchMin = 210f;
            bool  filterOneRow = rect.width >= searchMin + catW + mineW + guildW + sortW + 24f;
            float filterW = filterOneRow
                ? rect.width - catW - mineW - guildW - sortW - 24f
                : Mathf.Max(120f, rect.width - catW - 8f);

            _filter = DialogLayout.SearchField(new Rect(0f, y, filterW, 28f), _filter, "Filter by item or seller");

            if (DialogLayout.DrawDropdownButton(new Rect(filterW + 8f, y, catW, 28f), CategoryName(_categoryFilter)))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                foreach (string cat in CommonCategories)
                {
                    string captured = cat;
                    opts.Add(new FloatMenuOption(CategoryName(captured), () => _categoryFilter = captured));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            float togY = filterOneRow ? y : y + 32f;
            float cbx  = filterOneRow ? filterW + catW + 20f : 0f;
            cbx = DialogLayout.DrawTightCheckbox(cbx, togY + 4f, "Only mine",     ref _onlyMine);
            cbx = DialogLayout.DrawTightCheckbox(cbx, togY + 4f, "My guild only", ref _onlyMyGuild);

            if (rect.width - sortW - 4f >= cbx
                && Widgets.ButtonText(new Rect(rect.width - sortW, togY, sortW, 28f), $"Sort: {SortLabel(_sort)}"))
                DialogLayout.EnumFloatMenu<SortMode>(SortLabel, m => _sort = m);

            y = togY + DialogLayout.ToolbarRowH;

            // All four share the shrink evenly, so the left and right pairs never meet in the middle.
            float actW = Mathf.Max(
                Mathf.Max(IconButton.WidthFor("Want board", false), IconButton.WidthFor("Auctions", false)),
                Mathf.Max(IconButton.WidthFor("Refresh",    false), IconButton.WidthFor("Post listing", true)));
            actW = Mathf.Max(70f, Mathf.Min(actW, (rect.width - 32f) / 4f));

            if (Widgets.ButtonText(new Rect(0f, y, actW, 28f), "Want board"))
                Find.WindowStack.Add(new Features.WantBoard.Dialog_KMHWantBoard());
            if (Widgets.ButtonText(new Rect(actW + 8f, y, actW, 28f), "Auctions"))
                Find.WindowStack.Add(new Features.Auctions.Dialog_KMHAuctions());
            if (Widgets.ButtonText(new Rect(rect.width - actW * 2f - 8f, y, actW, 28f), "Refresh"))
                MarketplaceHandler.RequestSnapshot();
            if (IconButton.Draw(new Rect(rect.width - actW, y, actW, 28f), KMHTextures.Post, "Post listing"))
                Dialog_KMHPostListing.Open();

            y += 32f;

            DrawHeader(new Rect(0f, y, rect.width, 22f));
            y += 24f;

            Rect listBox = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(listBox);
            DrawRows(listBox, s);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        // Reserved for the per-row button; ColumnXs rebalances the rest so headers stay aligned with their cells.
        private const float ActionColumnWidth = 100f;

        private static void DrawHeader(Rect r)
        {
            float[] cols = ColumnXs(r.width);
            DialogLayout.LabelTrunc(new Rect(cols[0], r.y, cols[1] - cols[0], r.height), "<b>Item</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[1], r.y, cols[2] - cols[1], r.height), "<b>Qty</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[2], r.y, cols[3] - cols[2], r.height), "<b>Unit</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[3], r.y, cols[4] - cols[3], r.height), "<b>Total</b>");
            DialogLayout.LabelTrunc(new Rect(cols[4], r.y, cols[5] - cols[4], r.height), "<b>Seller</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[5], r.y, ListedW(r.width, cols[5]), r.height), "<b>Listed</b>");
            // Action column has no header text - buttons speak for themselves.
        }

        private void DrawRows(Rect box, MarketplaceSnapshot s)
        {
            const float rowH = DialogLayout.RowHeightSingle;

            string mine        = KmhSession.Me;
            string filterLower = (_filter ?? "").Trim().ToLower();

            // Null when the caller is in no guild.
            HashSet<string> myGuildMembers = null;
            if (_onlyMyGuild)
            {
                GuildSnapshot g = GuildCache.Guild;
                if (g?.Members != null)
                {
                    myGuildMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (GuildMemberDto m in g.Members) myGuildMembers.Add(m.Username);
                }
            }

            bool inputsChanged =
                _visible == null
                || !ReferenceEquals(_visibleSource, s.Listings)
                || _visibleFilter   != filterLower
                || _visibleCategory != _categoryFilter
                || _vcMine          != _onlyMine
                || _vcMyGuild       != _onlyMyGuild
                || _vcSort          != _sort;

            if (inputsChanged)
            {
                List<MarketplaceListing> source = s.Listings ?? new List<MarketplaceListing>();
                List<MarketplaceListing> filtered = new List<MarketplaceListing>(source.Count);

                for (int j = 0; j < source.Count; j++)
                {
                    MarketplaceListing r = source[j];

                    if (_onlyMine && (string.IsNullOrEmpty(mine)
                        || !KmhSession.Same(r.SellerUsername, mine)))
                        continue;

                    if (_onlyMyGuild && (myGuildMembers == null
                        || !myGuildMembers.Contains(r.SellerUsername)))
                        continue;

                    if (_categoryFilter != "All" && !MatchesCategory(r.ItemDefName, _categoryFilter))
                        continue;

                    if (filterLower.Length > 0)
                    {
                        string item   = ItemLabels.ResolveLabel(r.ItemDefName).ToLower();
                        string seller = (r.SellerUsername ?? "").ToLower();
                        if (!item.Contains(filterLower) && !seller.Contains(filterLower)
                            && !(r.ItemDefName ?? "").ToLower().Contains(filterLower))
                            continue;
                    }

                    filtered.Add(r);
                }

                switch (_sort)
                {
                    case SortMode.PriceAsc:  filtered.Sort((a, b) => a.EffectiveMilli.CompareTo(b.EffectiveMilli)); break;
                    case SortMode.PriceDesc: filtered.Sort((a, b) => b.EffectiveMilli.CompareTo(a.EffectiveMilli)); break;
                    case SortMode.QtyDesc:   filtered.Sort((a, b) => b.RemainingQty.CompareTo(a.RemainingQty));       break;
                    case SortMode.NameAsc:   filtered.Sort((a, b) => string.Compare(
                                                 ItemLabels.ResolveLabel(a.ItemDefName), ItemLabels.ResolveLabel(b.ItemDefName),
                                                 StringComparison.OrdinalIgnoreCase));                                break;
                    default:                 filtered.Sort((a, b) => b.ListedUtcTicks.CompareTo(a.ListedUtcTicks));   break;
                }

                _visible          = filtered;
                _visibleSource    = s.Listings;
                _visibleFilter    = filterLower;
                _visibleCategory  = _categoryFilter;
                _vcMine           = _onlyMine;
                _vcMyGuild        = _onlyMyGuild;
                _vcSort           = _sort;
            }

            List<MarketplaceListing> rows = _visible;
            long now = DateTime.UtcNow.Ticks;
            // 'mine' already computed above for the filter pass; reused here for the per-row 'is mine?' check

            // Naming the active filters, because an empty table under a filter otherwise looks like an empty market.
            string empty;
            if (s.Listings == null || s.Listings.Count == 0)
                empty = "<color=grey>No open listings. Be the first to sell something!</color>";
            else
            {
                List<string> active = new List<string>();
                if (!string.IsNullOrEmpty(filterLower)) active.Add($"the search \"{_filter.Trim()}\"");
                if (_categoryFilter != "All")           active.Add($"category {CategoryName(_categoryFilter)}");
                if (_onlyMine)                          active.Add("Only mine");
                if (_onlyMyGuild)                       active.Add("My guild only");
                empty = active.Count == 0
                    ? "<color=grey>No listings to show.</color>"
                    : $"<color=grey>{s.Listings.Count} listing{(s.Listings.Count == 1 ? "" : "s")} open, but none match {string.Join(" + ", active.ToArray())}.</color>";
            }

            // Only the visible rows draw - a busy/modded marketplace can have hundreds of listings.
            DialogLayout.ScrollList(box, ref _scroll, rows.Count, rowH, (i, row) =>
            {
                MarketplaceListing r = rows[i];
                float ly = row.y;
                float[] cols = ColumnXs(row.width);
                if (Mouse.IsOver(row))
                {
                    string demandTip = MarketDemand.Tip(r.ItemDefName);
                    if (!string.IsNullOrEmpty(demandTip)) TooltipHandler.TipRegion(row, demandTip);
                }

                const float iconSize = 22f;
                ItemLabels.DrawIcon(new Rect(cols[0] + 2f, ly + 1f, iconSize, iconSize), r.ItemDefName);
                UI.KmhItemInfo.ButtonForDef(cols[0] + iconSize + 4f, ly + 2f, r.ItemDefName, r.StuffDefName);

                string itemLabel = FormatItemName(r) + MarketDemand.Arrow(r.ItemDefName);
                if (r.IsAutoListing) itemLabel = $"<color=#9090ff>[auto]</color> {itemLabel}";
                if (!string.IsNullOrEmpty(r.StateNote)) itemLabel += $" <color=grey>({r.StateNote})</color>";   // full-state note (tainted/damaged/legacy)
                DialogLayout.LabelTrunc(new Rect(cols[0] + iconSize + UI.KmhItemInfo.Size + 8f, ly + 2f,
                    Mathf.Max(0f, cols[1] - cols[0] - iconSize - UI.KmhItemInfo.Size - 10f), rowH - 4f), itemLabel);

                DialogLayout.DrawCenteredLabel(new Rect(cols[1], ly + 2f, cols[2] - cols[1], rowH - 4f),
                    $"{r.RemainingQty}<color=grey>/{r.OriginalQty}</color>");
                DialogLayout.DrawCenteredLabel(new Rect(cols[2], ly + 2f, cols[3] - cols[2], rowH - 4f),
                    r.UnitPriceDisplay.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                DialogLayout.DrawCenteredLabel(new Rect(cols[3], ly + 2f, cols[4] - cols[3], rowH - 4f),
                    $"<b>{SilverFmt.Format(r.TotalAskingSilver(r.RemainingQty))}</b>");
                DialogLayout.LabelTrunc(new Rect(cols[4] + 4f, ly + 2f, cols[5] - cols[4] - 4f, rowH - 4f),
                    string.IsNullOrEmpty(r.SellerUsername) ? "<color=grey>-</color>" : LinkedAccountsCache.Format(r.SellerUsername));
                DialogLayout.DrawCenteredLabel(
                    new Rect(cols[5], ly + 2f, ListedW(row.width, cols[5]), rowH - 4f),
                    FormatListedExpires(r, now));

                // Action button column: Cancel for own listings, Buy for others.
                float aw = ActionW(row.width);
                Rect actionRect = new Rect(row.width - aw + 4f, ly + 2f, Mathf.Max(1f, aw - 8f), rowH - 4f);
                bool isMine = KmhSession.Same(r.SellerUsername, mine);
                if (isMine)
                {
                    if (IconButton.Draw(actionRect, KMHTextures.Cancel, "Cancel"))
                    {
                        MarketplaceHandler.TryCancel(r.Id);
                    }
                }
                else if (r.RemainingQty > 0)
                {
                    // Captured, or the lambda would read a later row by the time the player picks an amount.
                    MarketplaceListing captured = r;
                    if (IconButton.Draw(actionRect, KMHTextures.Buy, "Buy"))
                    {
                        Find.WindowStack.Add(new Dialog_KMHAmountInput(
                            title: $"Buy {FormatItemName(captured)} from {captured.SellerUsername}",
                            confirmLabel: "Buy",
                            unitLabel: "units",
                            maxHint: captured.RemainingQty,
                            onConfirm: qty => MarketplaceHandler.TryBuy(captured.Id, qty)));
                    }
                }
            }, empty);
        }

        internal static float[] ColumnXs(float w)
        {
            // The last column is sized first and the row laid out back from it, or it truncates against the action button.
            float listed = ListedColumnW(w);
            float right  = Mathf.Max(120f, w - ActionW(w) - listed - 4f);
            float body   = Mathf.Max(160f, right - 10f);
            return new float[]
            {
                10f,                  // 0 Item
                10f + body * 0.42f,   // 1 Qty
                10f + body * 0.54f,   // 2 Unit
                10f + body * 0.68f,   // 3 Total
                10f + body * 0.82f,   // 4 Seller
                right                 // 5 Listed/Expires (right-most)
            };
        }

        // Shrinks on a narrow window: a fixed width against a proportional grid drives the last column negative.
        private static float ActionW(float w) => Mathf.Min(ActionColumnWidth, Mathf.Max(48f, w * 0.14f));

        // Whatever is left between the last column and the action button, never negative.
        internal static float ListedW(float w, float col5) => Mathf.Max(0f, w - col5 - ActionW(w) - 4f);

        // Capped so a wide dialog spends the extra room on the item name rather than on a date.
        private static float ListedColumnW(float w) => Mathf.Clamp((w - ActionW(w)) * 0.17f, 96f, 190f);

        // "Other" is the catch-all, true for anything that matched none of these more specific categories.
        private static readonly System.Collections.Generic.HashSet<string> KnownCategoryDefs
            = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
              { "Resources", "Manufactured", "Foods", "Drugs", "Medicine",
                "Weapons", "Apparel", "Plants", "BodyParts" };

        private static bool MatchesCategory(string key, string category)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(category) || string.Equals(category, "All", StringComparison.OrdinalIgnoreCase)) return true;
            ItemKeys.Split(key, out string defName, out _, out _);   // composed def|stuff|quality -> base def
            ThingDef td;
            try { td = DefDatabase<ThingDef>.GetNamedSilentFail(defName); }
            catch { td = null; }
            bool other = string.Equals(category, "Other", StringComparison.OrdinalIgnoreCase);
            if (td?.thingCategories == null || td.thingCategories.Count == 0) return other;

            // Walk each direct category's ancestry so a top-level filter (Weapons) matches subcategory items (WeaponsMelee).
            foreach (ThingCategoryDef c in td.thingCategories)
                for (ThingCategoryDef cur = c; cur != null; cur = cur.parent)
                {
                    if (!other && string.Equals(cur.defName, category, StringComparison.OrdinalIgnoreCase)) return true;
                    if (other && KnownCategoryDefs.Contains(cur.defName)) return false;
                }
            return other;
        }

        private static string FormatItemName(MarketplaceListing r)
        {
            string baseName      = ItemLabels.ResolveStuffedLabel(r.ItemDefName, r.StuffDefName);
            string qualityPrefix = r.QualityIndex > 0 ? $"{ItemKeys.QualityName(r.QualityIndex)} " : "";
            return $"{qualityPrefix}{baseName}".Trim();
        }

        private static string FormatListedExpires(MarketplaceListing r, long nowTicks)
        {
            string listed = FormatAgo(r.ListedUtcTicks, nowTicks);
            string expires;
            if (r.ExpiresUtcTicks <= 0)
            {
                expires = "<color=grey>-</color>";
            }
            else if (nowTicks >= r.ExpiresUtcTicks)
            {
                expires = "<color=#ff8080>expired</color>";
            }
            else
            {
                expires = $"in {FormatDuration(r.ExpiresUtcTicks - nowTicks)}";
            }
            return $"{listed}  /  {expires}";
        }

        private static string FormatAgo(long utcTicks, long nowTicks)
        {
            if (utcTicks <= 0) return "<color=grey>-</color>";
            long delta = nowTicks - utcTicks;
            if (delta < 0) return "just now";
            return FormatDuration(delta) + " ago";
        }

        private static string FormatDuration(long ticks)
        {
            try
            {
                TimeSpan span = TimeSpan.FromTicks(ticks);
                if (span.TotalDays    >= 1) return $"{(int)span.TotalDays}d";
                if (span.TotalHours   >= 1) return $"{(int)span.TotalHours}h";
                if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
                return $"{(int)span.TotalSeconds}s";
            }
            catch { return "?"; }
        }
    }
}
