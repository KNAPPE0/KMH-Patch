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
                case SortMode.Newest:  return KmhText.Get("KMH_Market_SortNewest", "Newest");
                case SortMode.BidAsc:  return KmhText.Get("KMH_Auction_SortBidAsc", "Bid ↑");
                case SortMode.BidDesc: return KmhText.Get("KMH_Auction_SortBidDesc", "Bid ↓");
                case SortMode.MostBids:return KmhText.Get("KMH_Auction_SortMostBids", "Most bids");
                default:               return KmhText.Get("KMH_Auction_SortEndingSoon", "Ending soon");
            }
        }

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

        protected override Features.Discord.KmhActivity PresenceActivity => Features.Discord.KmhActivity.Auctions;

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_Auction_Title", "Auction House"), DialogLayout.LiveBadgeReserve);
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);
            DialogLayout.DrawSectionDivider(rect, ref y);

            string refreshLabel = KmhText.Get("KMH_GH_Refresh", "Refresh");
            string postLabel    = KmhText.Get("KMH_Auction_PostAuction", "Post auction");

            // The left group needs a fixed cursor width, so below that the right-anchored buttons wrap to their own row.
            float btnW = Mathf.Max(IconButton.WidthFor(refreshLabel, false), IconButton.WidthFor(postLabel, true));
            float leftNeeded = 534f;
            bool  oneRow     = rect.width >= leftNeeded + btnW * 2f + 8f;

            float leftW   = oneRow ? rect.width - btnW * 2f - 8f : rect.width;
            float filterW = Mathf.Clamp(leftW - 234f, 110f, 300f);

            _filter = DialogLayout.SearchField(new Rect(0f, y, filterW, 28f), _filter,
                KmhText.Get("KMH_Auction_FilterHint", "Filter by item or player"));
            float cx = DialogLayout.DrawTightCheckbox(filterW + 16f, y + 4f, KmhText.Get("KMH_Auction_Mine", "Mine"), ref _mineOnly);
            // Measured from the label: a fixed width cuts longer translations.
            string sortText = KmhText.Get("KMH_Browser_SortBy", "Sort: {0}", SortLabel(_sort));
            float sortW = Mathf.Clamp(leftW - cx - 8f, 0f, Mathf.Clamp(Text.CalcSize(sortText).x + 24f, 150f, 230f));
            if (sortW >= 90f && Widgets.ButtonText(new Rect(cx + 8f, y, sortW, 28f), sortText))
                DialogLayout.EnumFloatMenu<SortMode>(SortLabel, m => _sort = m);

            float btnY = oneRow ? y : y + 32f;
            float cell = oneRow ? btnW : rect.width / 2f;
            float btnX = oneRow ? rect.width - btnW * 2f : 0f;

            if (Widgets.ButtonText(new Rect(btnX, btnY, cell - 4f, 28f), refreshLabel))
            {
                AuctionHandler.RequestSnapshot();
            }
            if (IconButton.Draw(new Rect(btnX + cell, btnY, cell - 4f, 28f), KMHTextures.Post, postLabel))
                Dialog_KMHPostAuction.Open();

            y = btnY + 34f;

            Rect listBox = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(listBox);
            DrawList(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void DrawList(Rect box)
        {
            // Measured: a fixed 18f pitch under a 22f font overlaps every line.
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

            // Built only when it shows, so a full list allocates nothing for it.
            string empty = rows.Count > 0 ? null
                : !AuctionCache.HasSnapshot
                ? "<color=#A9AFB9>" + DialogLayout.AwaitingSnapshot(KmhText.Get("KMH_Auction_Loading", "Loading auctions…"),
                                                                    KmhText.Get("KMH_Report_Area_Auctions", "Auctions")) + "</color>"
                : (flt.Length > 0 || _mineOnly) ? "<color=#A9AFB9>" + KmhText.Get("KMH_Auction_NoMatch", "No auctions match.") + "</color>"
                : "<color=#A9AFB9>" + KmhText.Get("KMH_Auction_Empty", "No live auctions. Post one!") + "</color>";

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
            Rect titleR = new Rect(inner.x + 22f + UI.KmhItemInfo.Size + 4f, inner.y, textW - 22f - UI.KmhItemInfo.Size - 4f, DialogLayout.TextRowH);
            DialogLayout.LabelTrunc(titleR, KmhText.Get("KMH_Auction_RowTitle", "{0}  by {1}",
                "<b>#" + a.Id.ToString() + "  " + a.Qty.ToString() + "x " + ItemKeys.LabelForKey(a.ItemDefName) + "</b>",
                Seller(a.SellerUsername)));
            if (!mine) KmhPlayerMenu.LinkRegion(titleR, a.SellerUsername);

            string bidLine;
            if (hasBids)
            {
                string bid = "<color=yellow>" + SilverFmt.Format(a.CurrentBid) + "</color>";
                string you = iLead ? " <color=#4cc38a>" + KmhText.Get("KMH_Auction_You", "(you)") + "</color>" : "";
                bidLine = a.BidCount == 1
                    ? KmhText.Get("KMH_Auction_TopBidOne", "Top bid: {0} by {1}{2}  · 1 bid", bid, Seller(a.HighBidder), you)
                    : KmhText.Get("KMH_Auction_TopBidMany", "Top bid: {0} by {1}{2}  · {3} bids", bid, Seller(a.HighBidder), you, a.BidCount);
            }
            else
                bidLine = "<color=#A9AFB9>" + KmhText.Get("KMH_Auction_NoBids", "No bids · starts at {0}",
                    "<color=yellow>" + SilverFmt.Format(a.StartingBid) + "</color>") + "</color>";
            Color old = GUI.color; GUI.color = new Color(0.85f, 0.85f, 0.85f);
            Rect bidR = new Rect(inner.x, inner.y + DialogLayout.TextRowH, textW, DialogLayout.TextRowH);
            DialogLayout.LabelTrunc(bidR, bidLine);
            GUI.color = old;
            if (hasBids && !iLead) KmhPlayerMenu.LinkRegion(bidR, a.HighBidder);

            string ends  = DialogLayout.TimeLeft(a.EndsUtcTicks, now);
            string raise = SilverFmt.Format(a.MinIncrement);
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 2f, textW, DialogLayout.TextRowH),
                a.BuyoutSilver > 0
                    ? KmhText.Get("KMH_Auction_EndsBuyout", "Buyout {0}  ·  ends in {1}  ·  +{2} min raise",
                                  "<color=yellow>" + SilverFmt.Format(a.BuyoutSilver) + "</color>", ends, raise)
                    : KmhText.Get("KMH_Auction_Ends", "ends in {0}  ·  +{1} min raise", ends, raise));
            GUI.color = old;

            float bw = 106f, bx = inner.xMax - bw;
            if (mine)
            {
                if (!hasBids && IconButton.Draw(new Rect(bx, inner.y + 4f, bw, 24f), KMHTextures.Cancel, KmhText.Get("KMH_Common_Cancel", "Cancel")))
                    AuctionHandler.TryCancel(a.Id);
            }
            else
            {
                long minBid = hasBids ? a.CurrentBid + a.MinIncrement : a.StartingBid;
                string bidLabel    = KmhText.Get("KMH_Auction_Bid", "Bid");
                string buyoutLabel = KmhText.Get("KMH_Auction_Buyout", "Buyout");
                // A won lot lands in this game, so what it cannot load is not bid on.
                string cannot = UI.KmhItemDisplayService.MissingForListingCached(true, a.Id, a.ItemDefName, a.StuffDefName, a.EscrowPayloads);
                if (cannot != null)
                {
                    IconButton.DrawDisabled(new Rect(bx, inner.y + 4f, bw, 24f), KMHTextures.Post, bidLabel, cannot);
                    if (a.BuyoutSilver > 0) IconButton.DrawDisabled(new Rect(bx, inner.y + 32f, bw, 24f), KMHTextures.Buy, buyoutLabel, cannot);
                }
                else if (IconButton.Draw(new Rect(bx, inner.y + 4f, bw, 24f), KMHTextures.Post, bidLabel))
                {
                    long captured = a.Id;
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title:        KmhText.Get("KMH_Auction_BidTitle", "Bid on {0}x {1}", a.Qty, ItemKeys.LabelForKey(a.ItemDefName)),
                        confirmLabel: KmhText.Get("KMH_Auction_PlaceBid", "Place bid"),
                        unitLabel:    KmhText.Get("KMH_Auction_BidUnit", "silver (min {0})", SilverFmt.Format(minBid)),
                        maxHint:      0,
                        onConfirm:    amt => AuctionHandler.TryBid(captured, amt),
                        initial:      minBid.ToString()));
                }
                if (cannot == null && a.BuyoutSilver > 0 && IconButton.Draw(new Rect(bx, inner.y + 32f, bw, 24f), KMHTextures.Buy, buyoutLabel))
                {
                    long id = a.Id, price = a.BuyoutSilver;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        KmhText.Get("KMH_Auction_BuyoutConfirm", "Buy out this auction for {0} now?", SilverFmt.Format(price)),
                        () => AuctionHandler.TryBid(id, price)));
                }
            }
        }

        private static string Seller(string u)
            => LinkedAccountsCache.Format(u) + ReputationCache.Badge(u);   // Format()/Badge() both no-op on empty
    }
}
