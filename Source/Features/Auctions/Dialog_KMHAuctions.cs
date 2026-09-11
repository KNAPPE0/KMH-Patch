using System;
using System.Collections.Generic;
using KMHPatch.Features.Auctions.Dto;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Reputation;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Auctions
{
    public class Dialog_KMHAuctions : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(900f, 620f);

        private Vector2  _scroll;

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

        // Rebuilt only when the snapshot, filter or toggles change, never per frame.
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

            EnableAutoRefresh(() => AuctionHandler.RequestSnapshot());
            ReputationCache.RequestSnapshot();   // populate seller/bidder trust badges
            AuctionCache.Updated += MarkRefreshed;
        }

        public override void PostClose() { base.PostClose(); AuctionCache.Updated -= MarkRefreshed; }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Auction House");
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);
            DialogLayout.DrawSectionDivider(rect, ref y);

            // The left group needs a fixed cursor width, so below that the right-anchored buttons wrap to their own row.
            float btnW = Mathf.Max(IconButton.WidthFor("Refresh", false), IconButton.WidthFor("Post auction", true));
            float leftNeeded = 534f;
            bool  oneRow     = rect.width >= leftNeeded + btnW * 2f + 8f;

            float leftW   = oneRow ? rect.width - btnW * 2f - 8f : rect.width;
            float filterW = Mathf.Clamp(leftW - 234f, 110f, 300f);

            _filter = DialogLayout.SearchField(new Rect(0f, y, filterW, 28f), _filter, "Filter by item or player");
            float cx = DialogLayout.DrawTightCheckbox(filterW + 16f, y + 4f, "Mine", ref _mineOnly);
            float sortW = Mathf.Clamp(leftW - cx - 8f, 0f, 150f);
            if (sortW >= 90f && Widgets.ButtonText(new Rect(cx + 8f, y, sortW, 28f), $"Sort: {SortLabel(_sort)}"))
                DialogLayout.EnumFloatMenu<SortMode>(SortLabel, m => _sort = m);

            float btnY = oneRow ? y : y + 32f;
            float cell = oneRow ? btnW : rect.width / 2f;
            float btnX = oneRow ? rect.width - btnW * 2f : 0f;

            if (Widgets.ButtonText(new Rect(btnX, btnY, cell - 4f, 28f), "Refresh"))
            {
                AuctionHandler.RequestSnapshot();
            }
            if (IconButton.Draw(new Rect(btnX + cell, btnY, cell - 4f, 28f), KMHTextures.Post, "Post auction"))
                Dialog_KMHPostAuction.Open();

            y = btnY + 34f;

            Rect listBox = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(listBox);
            DrawList(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void DrawList(Rect box)
        {
            // Three stacked text lines plus padding, measured: an 18f pitch under a 22f font overlapped every line.
            float rowH = DialogLayout.TextRowsH(3, 8f);

            string me  = KmhSession.Me;
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

            string empty = !AuctionCache.HasSnapshot
                ? $"<color=grey>{DialogLayout.AwaitingSnapshot("Loading auctions…", "Auctions")}</color>"
                : (flt.Length > 0 || _mineOnly) ? "<color=grey>No auctions match.</color>"
                : "<color=grey>No live auctions. Post one!</color>";

            DialogLayout.ScrollList(box, ref _scroll, rows.Count, rowH, (i, row) =>
            {
                if (rows[i].EndsUtcTicks > 0 && rows[i].EndsUtcTicks - now < soon)
                    Widgets.DrawBoxSolid(row, new Color(0.95f, 0.55f, 0.2f, 0.10f));
                DrawRow(row, rows[i], me, now);
            }, empty);
        }

        private static List<AuctionDto> Build(List<AuctionDto> src, string flt, bool mineOnly, string me, SortMode sort)
        {
            List<AuctionDto> outList = new List<AuctionDto>();
            if (src == null) return outList;
            foreach (AuctionDto a in src)
            {
                if (a == null) continue;
                if (mineOnly && !(KmhSession.Same(a.SellerUsername, me) || KmhSession.Same(a.HighBidder, me))) continue;
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

        private static void DrawRow(Rect row, AuctionDto a, string me, long now)
        {
            Rect inner = row.ContractedBy(6f);
            const float reservedRight = 116f;
            // Floored: a negative width runs the text column back under the action buttons.
            float textW = Mathf.Max(0f, inner.width - reservedRight);

            bool mine    = KmhSession.Same(a.SellerUsername, me);
            bool hasBids = a.CurrentBid > 0 && !string.IsNullOrEmpty(a.HighBidder);
            bool iLead   = hasBids && KmhSession.Same(a.HighBidder, me);

            ItemKeys.Split(a.ItemDefName, out string rowDef, out string rowStuff, out _);
            ItemLabels.DrawIcon(new Rect(inner.x, inner.y, 20f, 20f), rowDef);
            UI.KmhItemInfo.ButtonForDef(inner.x + 22f, inner.y, rowDef, rowStuff);
            DialogLayout.LabelTrunc(new Rect(inner.x + 22f + UI.KmhItemInfo.Size + 4f, inner.y, textW - 22f - UI.KmhItemInfo.Size - 4f, DialogLayout.TextRowH),
                $"<b>#{a.Id}  {a.Qty}x {ItemKeys.LabelForKey(a.ItemDefName)}</b>  <color=grey>by</color> {Seller(a.SellerUsername)}");

            string bidLine = hasBids
                ? $"Top bid: <color=yellow>{SilverFmt.Format(a.CurrentBid)}</color> <color=grey>by</color> {Seller(a.HighBidder)}{(iLead ? " <color=#7CD37C>(you)</color>" : "")}  <color=grey>· {a.BidCount} bid(s)</color>"
                : $"<color=grey>No bids · starts at</color> <color=yellow>{SilverFmt.Format(a.StartingBid)}</color>";
            Color old = GUI.color; GUI.color = new Color(0.85f, 0.85f, 0.85f);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH, textW, DialogLayout.TextRowH), bidLine);
            GUI.color = old;

            string buyout = a.BuyoutSilver > 0 ? $"Buyout <color=yellow>{SilverFmt.Format(a.BuyoutSilver)}</color>  ·  " : "";
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 2f, textW, DialogLayout.TextRowH),
                $"{buyout}ends in {DialogLayout.TimeLeft(a.EndsUtcTicks, now)}  ·  +{SilverFmt.Format(a.MinIncrement)} min raise");
            GUI.color = old;

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
                if (a.BuyoutSilver > 0 && IconButton.Draw(new Rect(bx, inner.y + 32f, bw, 24f), KMHTextures.Buy, "Buyout"))
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
