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
        public override Vector2 InitialSize => new Vector2(1040f, 620f);

        private Vector2  _scroll;

        // Toolbar state. Categories are a fixed ThingCategoryDef list, so the filter works against any RimWorld
        // expansion / mod that places defs into them.
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
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh);
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (!MarketplaceCache.HasSnapshot)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 20f),
                    "<color=grey>Loading marketplace…</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            MarketplaceSnapshot s = MarketplaceCache.Snapshot;

            // Stats header - house pool + lifetime trades.
            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                $"House pool: {SilverFmt.Format(s.HouseSilverPool)}  |  " +
                $"Lifetime trades: {s.LifetimeTradesCompleted}  |  " +
                $"Lifetime silver traded: {SilverFmt.Format(s.LifetimeSilverTraded)}");
            GUI.color = oldCol;
            y += 24f;

            // Two-row toolbar (1040 px wide but filter + category + scope + 2 buttons still gets tight)
            //
            // Row 1: filter + category + Refresh / Post listing right.
            const float toolbarBtnW = 110f;
            _filter = DialogLayout.SearchField(new Rect(0f, y, 230f, 28f), _filter, "Filter by item or seller…");

            if (Widgets.ButtonText(new Rect(238f, y, 130f, 28f), $"Category: {_categoryFilter}"))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                foreach (string cat in CommonCategories)
                {
                    string captured = cat;
                    opts.Add(new FloatMenuOption(captured, () => _categoryFilter = captured));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            if (IconButton.Draw(new Rect(rect.width - toolbarBtnW, y, toolbarBtnW - 4f, 28f), KMHTextures.Post, "Post listing…"))
            {
                Dialog_KMHPostListing.Open();
            }
            if (Widgets.ButtonText(new Rect(rect.width - toolbarBtnW * 2f, y, toolbarBtnW - 4f, 28f), "Refresh"))
            {
                MarketplaceHandler.RequestSnapshot();
                MarkRefreshed();
            }
            if (Widgets.ButtonText(new Rect(rect.width - toolbarBtnW * 3f, y, toolbarBtnW - 4f, 28f), "Auctions…"))
            {
                Find.WindowStack.Add(new Features.Auctions.Dialog_KMHAuctions());
            }
            if (Widgets.ButtonText(new Rect(rect.width - toolbarBtnW * 4f, y, toolbarBtnW - 4f, 28f), "Want board…"))
            {
                Find.WindowStack.Add(new Features.WantBoard.Dialog_KMHWantBoard());
            }
            y += 32f;

            // Row 2: scope toggles (left) + sort (right).
            float cbx = 0f;
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Only mine",    ref _onlyMine);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "My guild only", ref _onlyMyGuild);

            if (Widgets.ButtonText(new Rect(rect.width - 160f, y, 156f, 26f), $"Sort: {SortLabel(_sort)}"))
                DialogLayout.EnumFloatMenu<SortMode>(SortLabel, m => _sort = m);
            y += 30f;

            DrawHeader(new Rect(0f, y, rect.width, 22f));
            y += 24f;

            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawRows(listBox, s);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        // Action column reserves trailing space for the per-row Buy/Cancel button. Other columns get rebalanced via
        // ColumnXs to keep header labels aligned with the cells
        private const float ActionColumnWidth = 100f;

        private static void DrawHeader(Rect r)
        {
            float[] cols = ColumnXs(r.width);
            DialogLayout.LabelTrunc(new Rect(cols[0], r.y, cols[1] - cols[0], r.height), "<b>Item</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[1], r.y, cols[2] - cols[1], r.height), "<b>Qty</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[2], r.y, cols[3] - cols[2], r.height), "<b>Unit</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[3], r.y, cols[4] - cols[3], r.height), "<b>Total</b>");
            DialogLayout.LabelTrunc(new Rect(cols[4], r.y, cols[5] - cols[4], r.height), "<b>Seller</b>");
            DialogLayout.DrawCenteredLabel(new Rect(cols[5], r.y, r.width - cols[5] - ActionColumnWidth, r.height), "<b>Listed / Expires</b>");
            // Action column has no header text - buttons speak for themselves.
        }

        private void DrawRows(Rect box, MarketplaceSnapshot s)
        {
            const float rowH = DialogLayout.RowHeightSingle;

            string mine        = KmhSession.Me;
            string filterLower = (_filter ?? "").Trim().ToLower();

            // 'My guild only' - names of guild members from the cached guild snapshot, or null when caller isn't in
            // a guild
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

            string empty = (s.Listings == null || s.Listings.Count == 0)
                ? "<color=grey>No open listings. Be the first to sell something!</color>"
                : "<color=grey>No listings match the filter.</color>";

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

                // Icon + info card + label in the Item column. Icon reserved as a 22px square; rest of the column is
                // the (possibly stuff+quality prefixed) label
                const float iconSize = 22f;
                ItemLabels.DrawIcon(new Rect(cols[0] + 2f, ly + 1f, iconSize, iconSize), r.ItemDefName);
                UI.KmhItemInfo.ButtonForDef(cols[0] + iconSize + 4f, ly + 2f, r.ItemDefName, r.StuffDefName);

                string itemLabel = FormatItemName(r) + MarketDemand.Arrow(r.ItemDefName);
                if (r.IsAutoListing) itemLabel = $"<color=#9090ff>[auto]</color> {itemLabel}";
                if (!string.IsNullOrEmpty(r.StateNote)) itemLabel += $" <color=grey>({r.StateNote})</color>";   // full-state note (tainted/damaged/legacy)
                DialogLayout.LabelTrunc(new Rect(cols[0] + iconSize + UI.KmhItemInfo.Size + 8f, ly + 2f,
                    cols[1] - cols[0] - iconSize - UI.KmhItemInfo.Size - 10f, rowH - 4f), itemLabel);

                DialogLayout.DrawCenteredLabel(new Rect(cols[1], ly + 2f, cols[2] - cols[1], rowH - 4f),
                    $"{r.RemainingQty}<color=grey>/{r.OriginalQty}</color>");
                DialogLayout.DrawCenteredLabel(new Rect(cols[2], ly + 2f, cols[3] - cols[2], rowH - 4f),
                    r.UnitPriceDisplay.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                DialogLayout.DrawCenteredLabel(new Rect(cols[3], ly + 2f, cols[4] - cols[3], rowH - 4f),
                    $"<b>{SilverFmt.Format(r.TotalAskingSilver(r.RemainingQty))}</b>");
                DialogLayout.LabelTrunc(new Rect(cols[4] + 4f, ly + 2f, cols[5] - cols[4] - 4f, rowH - 4f),
                    string.IsNullOrEmpty(r.SellerUsername) ? "<color=grey>-</color>" : LinkedAccountsCache.Format(r.SellerUsername));
                DialogLayout.DrawCenteredLabel(
                    new Rect(cols[5], ly + 2f, row.width - cols[5] - ActionColumnWidth, rowH - 4f),
                    FormatListedExpires(r, now));

                // Action button column: Cancel for own listings, Buy for others.
                Rect actionRect = new Rect(row.width - ActionColumnWidth + 4f, ly + 2f, ActionColumnWidth - 8f, rowH - 4f);
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
                    // Capture by local so the lambda doesn't reference the loop variable (would change as we
                    // iterate further rows before the user picks an amount)
                    MarketplaceListing captured = r;
                    if (IconButton.Draw(actionRect, KMHTextures.Buy, "Buy…"))
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

        private static float[] ColumnXs(float w)
        {
            // 6 cols: Item | Qty | Unit | Total | Seller | Listed/Expires
            return new float[]
            {
                10f,           // 0 Item
                w * 0.38f,     // 1 Qty
                w * 0.48f,     // 2 Unit
                w * 0.58f,     // 3 Total
                w * 0.68f,     // 4 Seller
                w * 0.84f      // 5 Listed/Expires (right-most)
            };
        }

        // True when the item's ThingDef belongs (transitively) to a ThingCategoryDef whose defName matches the
        // requested category. 'Other' is the catch-all: true for any item that didn't match a more specific
        // category.
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

        // Display name for a listing. Resolves defNames -> human labels via DefDatabase ("plasteel knife" for
        // ItemDefName=Knife + StuffDefName=Plasteel) and prepends quality where present ("Excellent plasteel
        // knife")
        private static string FormatItemName(MarketplaceListing r)
        {
            string baseName      = ItemLabels.ResolveStuffedLabel(r.ItemDefName, r.StuffDefName);
            string qualityPrefix = r.QualityIndex > 0 ? $"{QualityName(r.QualityIndex)} " : "";
            return $"{qualityPrefix}{baseName}".Trim();
        }

        // Mirror of RimWorld.QualityCategory ordering. 1..7 = Awful..Legendary.
        private static string QualityName(int idx)
        {
            switch (idx)
            {
                case 1: return "Awful";
                case 2: return "Poor";
                case 3: return "Normal";
                case 4: return "Good";
                case 5: return "Excellent";
                case 6: return "Masterwork";
                case 7: return "Legendary";
                default: return "";
            }
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
