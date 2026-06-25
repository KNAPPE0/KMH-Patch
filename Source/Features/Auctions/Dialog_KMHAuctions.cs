using System;
using System.Collections.Generic;
using GameClient.Misc;
using KMHPatch.Features.Auctions.Dto;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Reputation;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Auctions
{
    // Auction house for live bids, buyouts, posting, and canceling; server stays authoritative for every move.
    public class Dialog_KMHAuctions : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(900f, 620f);

        private Vector2  _scroll;
        private float    _refreshTimer   = DialogLayout.AutoRefreshSeconds;
        private DateTime _lastRefreshUtc = DateTime.UtcNow;

        private string _filter   = "";
        private bool   _mineOnly = false;

        private enum SortMode { EndingSoon, Newest, BidAsc, BidDesc, MostBids }
        private SortMode _sort = SortMode.EndingSoon;

        private static string SortLabel(SortMode m)
        {
            switch (m)
            {
                case SortMode.Newest:  return "Newest";
                case SortMode.BidAsc:  return "Bid ↑";
                case SortMode.BidDesc: return "Bid ↓";
                case SortMode.MostBids:return "Most bids";
                default:               return "Ending soon";
            }
        }

        // Cached filtered/sorted view - rebuilt only when the snapshot, filter, or toggles change (not every frame).
        private List<AuctionDto> _visible;
        private object _visibleSource;
        private string _visibleFilter;
        private bool   _visibleMine;
        private string _visibleMe;
        private SortMode _visibleSort;

        public Dialog_KMHAuctions()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            AuctionHandler.RequestSnapshot();
            ReputationCache.RequestSnapshot();   // populate seller/bidder trust badges
            _lastRefreshUtc   = DateTime.UtcNow;
            AuctionCache.Updated += OnUpdated;
        }

        public override void PostClose() { base.PostClose(); AuctionCache.Updated -= OnUpdated; }
        private void OnUpdated() => _lastRefreshUtc = DateTime.UtcNow;

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                AuctionHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Auction House");
            DialogLayout.DrawLiveBadge(rect, Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds));
            DialogLayout.DrawSectionDivider(rect, ref y);

            const float btnW = 130f;
            if (IconButton.Draw(new Rect(rect.width - btnW, y, btnW - 4f, 28f), KMHTextures.Post, "Post auction…"))
                Dialog_KMHPostAuction.Open();
            if (Widgets.ButtonText(new Rect(rect.width - btnW * 2f, y, btnW - 4f, 28f), "Refresh"))
            {
                AuctionHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
            _filter = DialogLayout.SearchField(new Rect(0f, y, 300f, 28f), _filter, "Filter by item or player…");
            float cx = DialogLayout.DrawTightCheckbox(316f, y + 4f, "Mine", ref _mineOnly);
            if (Widgets.ButtonText(new Rect(cx + 8f, y, 150f, 26f), $"Sort: {SortLabel(_sort)}"))
            {
                List<FloatMenuOption> sortOpts = new List<FloatMenuOption>();
                foreach (SortMode m in (SortMode[])Enum.GetValues(typeof(SortMode)))
                {
                    SortMode captured = m;
                    sortOpts.Add(new FloatMenuOption(SortLabel(captured), () => _sort = captured));
                }
                Find.WindowStack.Add(new FloatMenu(sortOpts));
            }
            y += 34f;

            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawList(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void DrawList(Rect box)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 62f;

            string me  = SessionHandler.Username ?? "";
            string flt = (_filter ?? "").Trim().ToLowerInvariant();
            object src = AuctionCache.HasSnapshot ? (object)AuctionCache.Snapshot.Auctions : null;

            if (_visible == null || !ReferenceEquals(_visibleSource, src)
                || _visibleFilter != flt || _visibleMine != _mineOnly || _visibleMe != me || _visibleSort != _sort)
            {
                _visible = Build(src as List<AuctionDto>, flt, _mineOnly, me, _sort);
                _visibleSource = src; _visibleFilter = flt; _visibleMine = _mineOnly; _visibleMe = me; _visibleSort = _sort;
            }
            List<AuctionDto> rows = _visible;

            long now = DateTime.UtcNow.Ticks;
            long soon = TimeSpan.FromMinutes(10).Ticks;
            float viewH = Mathf.Max(inner.height, rows.Count * rowH + 6f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            DialogLayout.VisibleRange(_scroll, inner.height, rowH, rows.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                Rect row = new Rect(0f, i * rowH, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                if (rows[i].EndsUtcTicks > 0 && rows[i].EndsUtcTicks - now < soon)
                    Widgets.DrawBoxSolid(row, new Color(0.95f, 0.55f, 0.2f, 0.10f)); // ending-soon tint
                Widgets.DrawHighlightIfMouseover(row);
                DrawRow(row, rows[i], me, now);
            }
            if (rows.Count == 0)
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f),
                    !AuctionCache.HasSnapshot ? $"<color=grey>{DialogLayout.AwaitingSnapshot("Loading auctions…", "Auctions")}</color>"
                    : (flt.Length > 0 || _mineOnly) ? "<color=grey>No auctions match.</color>"
                    : "<color=grey>No live auctions. Post one!</color>");
            Widgets.EndScrollView();
        }

        // Filter + sort. Built only on change, not per frame.
        private static List<AuctionDto> Build(List<AuctionDto> src, string flt, bool mineOnly, string me, SortMode sort)
        {
            List<AuctionDto> outList = new List<AuctionDto>();
            if (src == null) return outList;
            foreach (AuctionDto a in src)
            {
                if (a == null) continue;
                if (mineOnly && !(Eq(a.SellerUsername, me) || Eq(a.HighBidder, me))) continue;
                if (flt.Length > 0)
                {
                    string item = (ItemKeys.LabelForKey(a.ItemDefName) ?? "").ToLowerInvariant();
                    string sell = (a.SellerUsername ?? "").ToLowerInvariant();
                    if (!item.Contains(flt) && !sell.Contains(flt) && a.Id.ToString() != flt) continue;
                }
                outList.Add(a);
            }
            switch (sort)
            {
                case SortMode.Newest:   outList.Sort((x, z) => z.ListedUtcTicks.CompareTo(x.ListedUtcTicks)); break;
                case SortMode.BidAsc:   outList.Sort((x, z) => EffBid(x).CompareTo(EffBid(z)));               break;
                case SortMode.BidDesc:  outList.Sort((x, z) => EffBid(z).CompareTo(EffBid(x)));               break;
                case SortMode.MostBids: outList.Sort((x, z) => z.BidCount.CompareTo(x.BidCount));             break;
                default:                outList.Sort((x, z) => x.EndsUtcTicks.CompareTo(z.EndsUtcTicks));     break;
            }
            return outList;
        }

        // Sort key for price: the live top bid, or the starting bid when there are no bids yet.
        private static long EffBid(AuctionDto a) => a.CurrentBid > 0 ? a.CurrentBid : a.StartingBid;

        private static bool Eq(string x, string y) => !string.IsNullOrEmpty(x) && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

        private static void DrawRow(Rect row, AuctionDto a, string me, long now)
        {
            Rect inner = row.ContractedBy(6f);
            const float reservedRight = 116f;
            float textW = inner.width - reservedRight;

            bool mine    = !string.IsNullOrEmpty(me) && string.Equals(a.SellerUsername, me, StringComparison.OrdinalIgnoreCase);
            bool hasBids = a.CurrentBid > 0 && !string.IsNullOrEmpty(a.HighBidder);
            bool iLead   = hasBids && !string.IsNullOrEmpty(me) && string.Equals(a.HighBidder, me, StringComparison.OrdinalIgnoreCase);

            // Line 1: item + qty + seller
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, textW, 18f),
                $"<b>#{a.Id}  {a.Qty}x {ItemKeys.LabelForKey(a.ItemDefName)}</b>  <color=grey>by</color> {Seller(a.SellerUsername)}");

            // Line 2: current bid / starting + high bidder
            string bidLine = hasBids
                ? $"Top bid: <color=yellow>{SilverFmt.Format(a.CurrentBid)}</color> <color=grey>by</color> {Seller(a.HighBidder)}{(iLead ? " <color=#7CD37C>(you)</color>" : "")}  <color=grey>· {a.BidCount} bid(s)</color>"
                : $"<color=grey>No bids · starts at</color> <color=yellow>{SilverFmt.Format(a.StartingBid)}</color>";
            Color old = GUI.color; GUI.color = new Color(0.85f, 0.85f, 0.85f);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 18f, textW, 18f), bidLine);
            GUI.color = old;

            // Line 3: buyout + time left
            string buyout = a.BuyoutSilver > 0 ? $"Buyout <color=yellow>{SilverFmt.Format(a.BuyoutSilver)}</color>  ·  " : "";
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 36f, textW, 18f),
                $"{buyout}ends in {DialogLayout.TimeLeft(a.EndsUtcTicks, now)}  ·  +{SilverFmt.Format(a.MinIncrement)} min raise");
            GUI.color = old;

            // Actions
            float bw = 106f, bx = inner.xMax - bw;
            if (mine)
            {
                if (!hasBids && IconButton.Draw(new Rect(bx, inner.y + 4f, bw, 24f), KMHTextures.Cancel, "Cancel"))
                    AuctionHandler.TryCancel(a.Id);
            }
            else
            {
                long minBid = hasBids ? a.CurrentBid + a.MinIncrement : a.StartingBid;
                if (IconButton.Draw(new Rect(bx, inner.y + 4f, bw, 24f), KMHTextures.Post, "Bid"))
                {
                    long captured = a.Id;
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title:        $"Bid on {a.Qty}x {ItemKeys.LabelForKey(a.ItemDefName)}",
                        confirmLabel: "Place bid",
                        unitLabel:    $"silver (min {SilverFmt.Format(minBid)})",
                        maxHint:      0,
                        onConfirm:    amt => AuctionHandler.TryBid(captured, amt),
                        initial:      minBid.ToString()));
                }
                if (a.BuyoutSilver > 0 && IconButton.Draw(new Rect(bx, inner.y + 32f, bw, 24f), KMHTextures.Approve, "Buyout"))
                {
                    long id = a.Id, price = a.BuyoutSilver;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Buy out this auction for {SilverFmt.Format(price)} now?", () => AuctionHandler.TryBid(id, price)));
                }
            }
        }

        private static string Seller(string u)
            => LinkedAccountsCache.Format(u) + ReputationCache.Badge(u);   // Format()/Badge() both no-op on empty
    }
}
