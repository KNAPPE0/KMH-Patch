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
    public class Dialog_KMHMarketplace : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(Mathf.Clamp(Verse.UI.screenWidth * 0.62f, 1040f, 1400f),
                                                                                  Mathf.Clamp(Verse.UI.screenHeight * 0.78f, 620f, 920f));

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
                case SortMode.PriceAsc:  return KmhText.Get("KMH_Market_SortPriceAsc", "Price ↑");
                case SortMode.PriceDesc: return KmhText.Get("KMH_Market_SortPriceDesc", "Price ↓");
                case SortMode.QtyDesc:   return KmhText.Get("KMH_Market_SortQtyDesc", "Qty ↓");
                case SortMode.NameAsc:   return KmhText.Get("KMH_Market_SortNameAsc", "Name A-Z");
                default:                 return KmhText.Get("KMH_Market_SortNewest", "Newest");
            }
        }

        private static readonly SortMode[] SortModes =
            { SortMode.Newest, SortMode.PriceAsc, SortMode.PriceDesc, SortMode.QtyDesc, SortMode.NameAsc };

        private static readonly string[] CommonCategories =
        {
            "All", "Resources", "Manufactured", "Foods", "Drugs",
            "Medicine", "Weapons", "Apparel", "Plants", "BodyParts", "Other"
        };

        // The stored values are ThingCategoryDef defNames, which the filter matches on; only the display differs.
        private static string CategoryName(string cat)
        {
            switch (cat)
            {
                case "All":          return KmhText.Get("KMH_Market_CatAll", "All categories");
                case "Resources":    return KmhText.Get("KMH_Market_CatResources", "Resources");
                case "Manufactured": return KmhText.Get("KMH_Market_CatManufactured", "Manufactured");
                case "Foods":        return KmhText.Get("KMH_Market_CatFoods", "Foods");
                case "Drugs":        return KmhText.Get("KMH_Market_CatDrugs", "Drugs");
                case "Medicine":     return KmhText.Get("KMH_Market_CatMedicine", "Medicine");
                case "Weapons":      return KmhText.Get("KMH_Market_CatWeapons", "Weapons");
                case "Apparel":      return KmhText.Get("KMH_Market_CatApparel", "Apparel");
                case "Plants":       return KmhText.Get("KMH_Market_CatPlants", "Plants");
                case "BodyParts":    return KmhText.Get("KMH_Market_CatBodyParts", "Body parts");
                case "Other":        return KmhText.Get("KMH_Market_CatOther", "Other");
                default:             return cat;
            }
        }

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

        private readonly RowTextCache _rowText = new RowTextCache();
        private object         _statsFor;
        private LoadedLanguage _statsLanguage;
        private string         _stats;
        private float          _colsWidth = -1f;
        private float[]        _cols;

        internal sealed class RowText { public string Item, Qty, Unit, Total, Listed; public long Second = -1; }

        // A row's text is built once per snapshot, language and second, not on every frame it is drawn.
        internal sealed class RowTextCache
        {
            private readonly Dictionary<long, RowText> _rows = new Dictionary<long, RowText>();
            private object         _source;
            private LoadedLanguage _language;
            private bool           _english;

            internal RowText TextFor(MarketplaceListing r, object source, long now)
            {
                if (!ReferenceEquals(_source, source) || _language != LanguageDatabase.activeLanguage || _english != KmhText.EnglishOnlyForTest)
                {
                    _rows.Clear();
                    _source = source; _language = LanguageDatabase.activeLanguage; _english = KmhText.EnglishOnlyForTest;
                }
                if (!_rows.TryGetValue(r.Id, out RowText t))
                {
                    t = new RowText
                    {
                        Qty   = $"{r.RemainingQty}<color=#A9AFB9>/{r.OriginalQty}</color>",
                        Unit  = UnitText(r.UnitPriceDisplay),
                        Total = $"<b>{SilverFmt.Format(r.TotalAskingSilver(r.RemainingQty))}</b>",
                    };
                    _rows[r.Id] = t;
                }
                long second = now / TimeSpan.TicksPerSecond;
                if (t.Second != second)
                {
                    // The demand arrow and the times change without a new snapshot.
                    string item = FormatItemName(r) + MarketDemand.Arrow(r.ItemDefName);
                    if (r.IsAutoListing) item = "<color=#9090ff>" + KmhText.Get("KMH_Market_AutoTag", "[auto]") + "</color> " + item;
                    if (!string.IsNullOrEmpty(r.StateNote)) item += $" <color=#A9AFB9>({r.StateNote})</color>";
                    t.Item = item; t.Listed = FormatListedExpires(r, now); t.Second = second;
                }
                return t;
            }
        }

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

        private void OnSnapshotUpdated()
        {
            _visible = null;
            MarkRefreshed();
        }

        protected override Features.Discord.KmhActivity PresenceActivity => Features.Discord.KmhActivity.Marketplace;

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_Market_Title", "Player Marketplace"), DialogLayout.LiveBadgeReserve);
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (!MarketplaceCache.HasSnapshot)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 20f),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_Market_Loading", "Loading marketplace…") + "</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            MarketplaceSnapshot s = MarketplaceCache.Snapshot;

            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            if (!ReferenceEquals(_statsFor, s) || _statsLanguage != LanguageDatabase.activeLanguage)
            {
                _stats = KmhText.Get("KMH_Market_Stats", "House pool: {0}  |  Lifetime trades: {1}  |  Lifetime silver traded: {2}",
                    SilverFmt.Format(s.HouseSilverPool), s.LifetimeTradesCompleted, SilverFmt.Format(s.LifetimeSilverTraded));
                _statsFor = s; _statsLanguage = LanguageDatabase.activeLanguage;
            }
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f), _stats);
            GUI.color = oldCol;
            y += 24f;

            float catW = DialogLayout.DropdownWidth(CategoryNames(), 110f, 200f);

            string onlyMine    = KmhText.Get("KMH_Market_OnlyMine", "Only mine");
            string myGuildOnly = KmhText.Get("KMH_Market_MyGuildOnly", "My guild only");
            float mineW  = DialogLayout.TightCheckboxWidth(onlyMine);
            float guildW = DialogLayout.TightCheckboxWidth(myGuildOnly);
            // Sized for the widest option, so the toolbar does not shift when the sort changes.
            float sortLabelW = 0f;
            foreach (SortMode m in SortModes) sortLabelW = Mathf.Max(sortLabelW, Text.CalcSize(SortLabel(m)).x);
            float sortW  = Mathf.Clamp(Text.CalcSize(KmhText.Get("KMH_Browser_SortBy", "Sort: {0}", "")).x + sortLabelW + 26f, 110f, 170f);

            // The toggles and sort take a row of their own before the search field is squeezed below readability.
            const float searchMin = 210f;
            bool  filterOneRow = rect.width >= searchMin + catW + mineW + guildW + sortW + 24f;
            float filterW = filterOneRow
                ? rect.width - catW - mineW - guildW - sortW - 24f
                : Mathf.Max(120f, rect.width - catW - 8f);

            _filter = DialogLayout.SearchField(new Rect(0f, y, filterW, 28f), _filter,
                KmhText.Get("KMH_Market_FilterHint", "Filter by item or seller"));

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
            cbx = DialogLayout.DrawTightCheckbox(cbx, togY + 4f, onlyMine,    ref _onlyMine);
            cbx = DialogLayout.DrawTightCheckbox(cbx, togY + 4f, myGuildOnly, ref _onlyMyGuild);

            if (rect.width - sortW - 4f >= cbx
                && Widgets.ButtonText(new Rect(rect.width - sortW, togY, sortW, 28f),
                                      KmhText.Get("KMH_Browser_SortBy", "Sort: {0}", SortLabel(_sort))))
                DialogLayout.EnumFloatMenu<SortMode>(SortLabel, m => _sort = m);

            y = togY + DialogLayout.ToolbarRowH;

            string wantLabel    = KmhText.Get("KMH_Report_Area_WantBoard", "Want Board");
            string auctionLabel = KmhText.Get("KMH_Report_Area_Auctions", "Auctions");
            string refreshLabel = KmhText.Get("KMH_GH_Refresh", "Refresh");
            string postLabel    = KmhText.Get("KMH_Market_PostListing", "Post listing");

            // All four share the shrink evenly, so the left and right pairs never meet in the middle.
            float actW = Mathf.Max(
                Mathf.Max(IconButton.WidthFor(wantLabel,    false), IconButton.WidthFor(auctionLabel, false)),
                Mathf.Max(IconButton.WidthFor(refreshLabel, false), IconButton.WidthFor(postLabel,    true)));
            actW = Mathf.Max(70f, Mathf.Min(actW, (rect.width - 32f) / 4f));

            if (Widgets.ButtonText(new Rect(0f, y, actW, 28f), wantLabel))
                Find.WindowStack.Add(new Features.WantBoard.Dialog_KMHWantBoard());
            if (Widgets.ButtonText(new Rect(actW + 8f, y, actW, 28f), auctionLabel))
                Find.WindowStack.Add(new Features.Auctions.Dialog_KMHAuctions());
            if (Widgets.ButtonText(new Rect(rect.width - actW * 2f - 8f, y, actW, 28f), refreshLabel))
                MarketplaceHandler.RequestSnapshot();
            if (IconButton.Draw(new Rect(rect.width - actW, y, actW, 28f), KMHTextures.Post, postLabel))
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
            DialogLayout.LabelTrunc(new Rect(cols[0], r.y, cols[1] - cols[0], r.height),
                "<b>" + KmhText.Get("KMH_Market_ColItem", "Item") + "</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[1], r.y, cols[2] - cols[1], r.height),
                "<b>" + KmhText.Get("KMH_Market_ColQty", "Qty") + "</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[2], r.y, cols[3] - cols[2], r.height),
                "<b>" + KmhText.Get("KMH_Market_ColUnit", "Unit") + "</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[3], r.y, cols[4] - cols[3], r.height),
                "<b>" + KmhText.Get("KMH_Market_ColTotal", "Total") + "</b>");
            DialogLayout.LabelTrunc(new Rect(cols[4], r.y, cols[5] - cols[4], r.height),
                "<b>" + KmhText.Get("KMH_Market_ColSeller", "Seller") + "</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[5], r.y, ListedW(r.width, cols[5]), r.height),
                "<b>" + KmhText.Get("KMH_Market_ColListed", "Listed") + "</b>");
        }

        private void DrawRows(Rect box, MarketplaceSnapshot s)
        {
            const float rowH = DialogLayout.RowHeightSingle;

            string mine        = KmhSession.Me;
            string filterLower = (_filter ?? "").Trim().ToLower();

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

            // Names the active filters, or an empty table under a filter looks like an empty market.
            string empty = null;
            if (rows.Count == 0)
            {
                if (s.Listings == null || s.Listings.Count == 0)
                    empty = "<color=#A9AFB9>" + KmhText.Get("KMH_Market_EmptyNone", "No open listings. Be the first to sell something!") + "</color>";
                else
                {
                    List<string> active = new List<string>();
                    if (!string.IsNullOrEmpty(filterLower)) active.Add(KmhText.Get("KMH_Market_FilterSearch", "the search \"{0}\"", _filter.Trim()));
                    if (_categoryFilter != "All")           active.Add(KmhText.Get("KMH_Market_FilterCategory", "category {0}", CategoryName(_categoryFilter)));
                    if (_onlyMine)                          active.Add(KmhText.Get("KMH_Market_OnlyMine", "Only mine"));
                    if (_onlyMyGuild)                       active.Add(KmhText.Get("KMH_Market_MyGuildOnly", "My guild only"));
                    int open = s.Listings.Count;
                    empty = active.Count == 0
                        ? "<color=#A9AFB9>" + KmhText.Get("KMH_Market_EmptyFiltered", "No listings to show.") + "</color>"
                        : "<color=#A9AFB9>" + (open == 1
                            ? KmhText.Get("KMH_Market_NoMatchOne", "1 listing open, but it does not match {0}.", string.Join(" + ", active.ToArray()))
                            : KmhText.Get("KMH_Market_NoMatchMany", "{0} listings open, but none match {1}.", open, string.Join(" + ", active.ToArray())))
                          + "</color>";
                }
            }

            // Only the visible rows draw - a busy/modded marketplace can have hundreds of listings.
            DialogLayout.ScrollList(box, ref _scroll, rows.Count, rowH, (i, row) =>
            {
                MarketplaceListing r = rows[i];
                float ly = row.y;
                if (row.width != _colsWidth) { _cols = ColumnXs(row.width); _colsWidth = row.width; }
                float[] cols = _cols;
                RowText text = _rowText.TextFor(r, s.Listings, now);
                if (Mouse.IsOver(row))
                {
                    string demandTip = MarketDemand.Tip(r.ItemDefName);
                    if (!string.IsNullOrEmpty(demandTip)) TooltipHandler.TipRegion(row, demandTip);
                }

                const float iconSize = 22f;
                ItemLabels.DrawIcon(new Rect(cols[0] + 2f, ly + 1f, iconSize, iconSize), r.ItemDefName);
                UI.KmhItemInfo.ButtonForDef(cols[0] + iconSize + 4f, ly + 2f, r.ItemDefName, r.StuffDefName);

                DialogLayout.LabelTrunc(new Rect(cols[0] + iconSize + UI.KmhItemInfo.Size + 8f, ly + 2f,
                    Mathf.Max(0f, cols[1] - cols[0] - iconSize - UI.KmhItemInfo.Size - 10f), rowH - 4f), text.Item);

                DialogLayout.DrawCenteredLabel(new Rect(cols[1], ly + 2f, cols[2] - cols[1], rowH - 4f), text.Qty);
                DialogLayout.DrawCenteredLabel(new Rect(cols[2], ly + 2f, cols[3] - cols[2], rowH - 4f), text.Unit);
                DialogLayout.DrawCenteredLabel(new Rect(cols[3], ly + 2f, cols[4] - cols[3], rowH - 4f), text.Total);
                KmhPlayerMenu.NameCell(new Rect(cols[4] + 4f, ly + 2f, cols[5] - cols[4] - 4f, rowH - 4f), r.SellerUsername);
                DialogLayout.DrawCenteredLabel(
                    new Rect(cols[5], ly + 2f, ListedW(row.width, cols[5]), rowH - 4f), text.Listed);

                float aw = ActionW(row.width);
                Rect actionRect = new Rect(row.width - aw + 4f, ly + 2f, Mathf.Max(1f, aw - 8f), rowH - 4f);
                bool isMine = KmhSession.Same(r.SellerUsername, mine);
                if (isMine)
                {
                    if (IconButton.Draw(actionRect, KMHTextures.Cancel, KmhText.Get("KMH_Common_Cancel", "Cancel")))
                    {
                        MarketplaceHandler.TryCancel(r.Id);
                    }
                }
                else if (r.RemainingQty > 0)
                {
                    // Captured, or the lambda would read a later row by the time the player picks an amount.
                    MarketplaceListing captured = r;
                    // Bought goods land in this game, so what it cannot load is not offered.
                    string cannot = KmhItemDisplayService.MissingForListingCached(false, r.Id, r.ItemDefName, r.StuffDefName, r.EscrowPayloads);
                    string buyLabel = KmhText.Get("KMH_GH_Buy", "Buy");
                    if (cannot != null) IconButton.DrawDisabled(actionRect, KMHTextures.Buy, buyLabel, cannot);
                    else if (IconButton.Draw(actionRect, KMHTextures.Buy, buyLabel))
                    {
                        Find.WindowStack.Add(new Dialog_KMHAmountInput(
                            title: KmhText.Get("KMH_Market_BuyTitle", "Buy {0} from {1}",
                                               FormatItemName(captured), KmhDisplayText.Inert(captured.SellerUsername)),
                            confirmLabel: buyLabel,
                            unitLabel: KmhText.Get("KMH_Market_Units", "units"),
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
                10f,
                10f + body * 0.42f,
                10f + body * 0.54f,
                10f + body * 0.68f,
                10f + body * 0.82f,
                right
            };
        }

        // Shrinks on a narrow window: a fixed width against a proportional grid drives the last column negative.
        private static float ActionW(float w) => Mathf.Min(ActionColumnWidth, Mathf.Max(48f, w * 0.14f));

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
            ItemKeys.Split(key, out string defName, out _, out _);
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
                expires = "<color=#A9AFB9>-</color>";
            }
            else if (nowTicks >= r.ExpiresUtcTicks)
            {
                expires = "<color=#f07167>" + KmhText.Get("KMH_Dialog_Expired", "expired") + "</color>";
            }
            else
            {
                expires = KmhText.Get("KMH_Market_ExpiresIn", "in {0}", FormatDuration(r.ExpiresUtcTicks - nowTicks));
            }
            return $"{listed}  /  {expires}";
        }

        private static string FormatAgo(long utcTicks, long nowTicks)
        {
            if (utcTicks <= 0) return "<color=#A9AFB9>-</color>";
            long delta = nowTicks - utcTicks;
            if (delta < 0) return KmhText.Get("KMH_Ago_JustNow", "just now");
            return KmhText.Get("KMH_Ago_Ago", "{0} ago", FormatDuration(delta));
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

        // Fewer decimals as the price grows: 10.756 read as noise, 0.125 still needs all three.
        internal static string UnitText(double unit)
            => unit.ToString(unit >= 10 ? "0.#" : unit >= 1 ? "0.##" : "0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
