using System;
using System.Collections.Generic;
using GameClient.Misc;
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
    // Marketplace browse: 1040x620 with a stats header and per-listing rows. Buy… and Cancel actions are wired
    // (visible by ownership: your own listing = Cancel, others = Buy…). Toolbar has the Post listing… composer
    // opener and Refresh.
    //
    // Default sort: ListedUtcTicks desc (newest first).
    public class Dialog_KMHMarketplace : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(1040f, 620f);

        private Vector2  _scroll;
        private float    _refreshTimer   = DialogLayout.AutoRefreshSeconds;
        private DateTime _lastRefreshUtc = DateTime.UtcNow;

        // Toolbar state. Categories are a fixed ThingCategoryDef list, so the filter works against any RimWorld
        // expansion / mod that places defs into them.
        private string _filter         = "";
        private string _categoryFilter = "All";
        private bool   _onlyMine       = false;
        private bool   _onlyMyGuild    = false;

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

        public Dialog_KMHMarketplace()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            MarketplaceHandler.RequestSnapshot();
            _lastRefreshUtc       = DateTime.UtcNow;
            MarketplaceCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            MarketplaceCache.Updated -= OnSnapshotUpdated;
        }

        private void OnSnapshotUpdated()
        {
            _visible        = null;
            _lastRefreshUtc = DateTime.UtcNow;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                MarketplaceHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Player Marketplace");
            int secsSince = Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds);
            DialogLayout.DrawLiveBadge(rect, secsSince);
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
                $"House pool: {s.HouseSilverPool} silver  |  " +
                $"Lifetime trades: {s.LifetimeTradesCompleted}  |  " +
                $"Lifetime silver traded: {s.LifetimeSilverTraded}");
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
                _lastRefreshUtc = DateTime.UtcNow;
            }
            y += 32f;

            // Row 2: scope toggles.
            float cbx = 0f;
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "Only mine",    ref _onlyMine);
            cbx = DialogLayout.DrawTightCheckbox(cbx, y + 4f, "My guild only", ref _onlyMyGuild);
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
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = DialogLayout.RowHeightSingle;

            string mine        = SessionHandler.Username ?? string.Empty;
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
                || _vcMyGuild       != _onlyMyGuild;

            if (inputsChanged)
            {
                List<MarketplaceListing> source = s.Listings ?? new List<MarketplaceListing>();
                List<MarketplaceListing> filtered = new List<MarketplaceListing>(source.Count);

                for (int j = 0; j < source.Count; j++)
                {
                    MarketplaceListing r = source[j];

                    if (_onlyMine && (string.IsNullOrEmpty(mine)
                        || !string.Equals(r.SellerUsername, mine, StringComparison.OrdinalIgnoreCase)))
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

                filtered.Sort((a, b) => b.ListedUtcTicks.CompareTo(a.ListedUtcTicks));

                _visible          = filtered;
                _visibleSource    = s.Listings;
                _visibleFilter    = filterLower;
                _visibleCategory  = _categoryFilter;
                _vcMine           = _onlyMine;
                _vcMyGuild        = _onlyMyGuild;
            }

            List<MarketplaceListing> rows = _visible;
            float viewH    = Mathf.Max(inner.height, rows.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float   ly   = 0f;
            float[] cols = ColumnXs(viewRect.width);
            long    now  = DateTime.UtcNow.Ticks;
            // 'mine' already computed above for the filter pass; reused here for the per-row 'is mine?' check

            for (int i = 0; i < rows.Count; i++)
            {
                MarketplaceListing r = rows[i];
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                // Icon + label in the Item column. Icon reserved as a 22px square; rest of the column is the
                // (possibly stuff+quality prefixed) label
                const float iconSize = 22f;
                ItemLabels.DrawIcon(new Rect(cols[0] + 2f, ly + 1f, iconSize, iconSize), r.ItemDefName);

                string itemLabel = FormatItemName(r);
                if (r.IsAutoListing) itemLabel = $"<color=#9090ff>[auto]</color> {itemLabel}";
                DialogLayout.LabelTrunc(new Rect(cols[0] + iconSize + 6f, ly + 2f, cols[1] - cols[0] - iconSize - 8f, rowH - 4f), itemLabel);

                DialogLayout.DrawCenteredLabel(new Rect(cols[1], ly + 2f, cols[2] - cols[1], rowH - 4f),
                    $"{r.RemainingQty}<color=grey>/{r.OriginalQty}</color>");
                DialogLayout.DrawCenteredLabel(new Rect(cols[2], ly + 2f, cols[3] - cols[2], rowH - 4f),
                    $"{SilverFmt.Format(r.UnitPriceSilver)}");
                DialogLayout.DrawCenteredLabel(new Rect(cols[3], ly + 2f, cols[4] - cols[3], rowH - 4f),
                    $"<b>{r.TotalAskingSilver(r.RemainingQty)}s</b>");
                DialogLayout.LabelTrunc(new Rect(cols[4] + 4f, ly + 2f, cols[5] - cols[4] - 4f, rowH - 4f),
                    string.IsNullOrEmpty(r.SellerUsername) ? "<color=grey>-</color>" : LinkedAccountsCache.Format(r.SellerUsername));
                DialogLayout.DrawCenteredLabel(
                    new Rect(cols[5], ly + 2f, viewRect.width - cols[5] - ActionColumnWidth, rowH - 4f),
                    FormatListedExpires(r, now));

                // Action button column: Cancel for own listings, Buy for others.
                Rect actionRect = new Rect(viewRect.width - ActionColumnWidth + 4f, ly + 2f, ActionColumnWidth - 8f, rowH - 4f);
                bool isMine = !string.IsNullOrEmpty(mine) && string.Equals(r.SellerUsername, mine, StringComparison.OrdinalIgnoreCase);
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

                ly += rowH;
            }
            if (rows.Count == 0)
            {
                bool noData = s.Listings == null || s.Listings.Count == 0;
                string msg = noData
                    ? "<color=grey>No open listings. Be the first to sell something!</color>"
                    : "<color=grey>No listings match the filter.</color>";
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f), msg);
            }

            Widgets.EndScrollView();
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

        private static bool MatchesCategory(string defName, string category)
        {
            if (string.IsNullOrEmpty(defName) || string.IsNullOrEmpty(category)) return true;
            ThingDef td;
            try { td = DefDatabase<ThingDef>.GetNamedSilentFail(defName); }
            catch { return false; }
            if (td?.thingCategories == null) return string.Equals(category, "Other", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(category, "Other", StringComparison.OrdinalIgnoreCase))
            {
                // 'Other' matches when the def belongs to no known category.
                for (int i = 0; i < td.thingCategories.Count; i++)
                {
                    if (KnownCategoryDefs.Contains(td.thingCategories[i].defName)) return false;
                }
                return true;
            }

            for (int i = 0; i < td.thingCategories.Count; i++)
            {
                if (string.Equals(td.thingCategories[i].defName, category, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
