using System;
using System.Collections.Generic;
using GameClient.Misc;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.WantBoard.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.WantBoard
{
    // Want-to-buy board: browse open buy requests, fulfill them from your treasury for the posted price, post + cancel
    // your own. The buyer's silver is escrowed server-side; fulfilling moves items treasury->treasury, so every move is server-authoritative.
    public class Dialog_KMHWantBoard : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(900f, 620f);

        private Vector2  _scroll;
        private float    _refreshTimer   = DialogLayout.AutoRefreshSeconds;
        private DateTime _lastRefreshUtc = DateTime.UtcNow;

        private string _filter   = "";
        private bool   _mineOnly = false;

        // Cached filtered/sorted view - rebuilt only when the snapshot, filter, or toggle change (not every frame).
        private List<WantDto> _visible;
        private object _visibleSource;
        private string _visibleFilter;
        private bool   _visibleMine;
        private string _visibleMe;

        public Dialog_KMHWantBoard()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            WantHandler.RequestSnapshot();
            _lastRefreshUtc   = DateTime.UtcNow;
            WantCache.Updated += OnUpdated;
        }

        public override void PostClose() { base.PostClose(); WantCache.Updated -= OnUpdated; }
        private void OnUpdated() => _lastRefreshUtc = DateTime.UtcNow;

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                WantHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Want Board");
            DialogLayout.DrawLiveBadge(rect, Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds));
            DialogLayout.DrawSectionDivider(rect, ref y);

            const float btnW = 130f;
            if (IconButton.Draw(new Rect(rect.width - btnW, y, btnW - 4f, 28f), KMHTextures.Post, "Post want…"))
                Dialog_KMHPostWant.Open();
            if (Widgets.ButtonText(new Rect(rect.width - btnW * 2f, y, btnW - 4f, 28f), "Refresh"))
            {
                WantHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
            _filter = DialogLayout.SearchField(new Rect(0f, y, 300f, 28f), _filter, "Filter by item or player…");
            DialogLayout.DrawTightCheckbox(316f, y + 4f, "Mine", ref _mineOnly);
            y += 34f;

            Color old = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f),
                "Deliver from your treasury (deposit the goods to your vault first) to fulfill a want for its posted price.");
            GUI.color = old;
            y += 20f;

            Rect listBox = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(listBox);
            DrawList(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void DrawList(Rect box)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 58f;

            string me  = SessionHandler.Username ?? "";
            string flt = (_filter ?? "").Trim().ToLowerInvariant();
            object src = WantCache.HasSnapshot ? (object)WantCache.Snapshot.Wants : null;

            if (_visible == null || !ReferenceEquals(_visibleSource, src)
                || _visibleFilter != flt || _visibleMine != _mineOnly || _visibleMe != me)
            {
                _visible = Build(src as List<WantDto>, flt, _mineOnly, me);
                _visibleSource = src; _visibleFilter = flt; _visibleMine = _mineOnly; _visibleMe = me;
            }
            List<WantDto> rows = _visible;

            long now = DateTime.UtcNow.Ticks;
            float viewH = Mathf.Max(inner.height, rows.Count * rowH + 6f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            DialogLayout.VisibleRange(_scroll, inner.height, rowH, rows.Count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                Rect row = new Rect(0f, i * rowH, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);
                DrawRow(row, rows[i], me, now);
            }
            if (rows.Count == 0)
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f),
                    !WantCache.HasSnapshot ? $"<color=grey>{DialogLayout.AwaitingSnapshot("Loading wants…", "Wants")}</color>"
                    : (flt.Length > 0 || _mineOnly) ? "<color=grey>No wants match.</color>"
                    : "<color=grey>No open wants. Post one!</color>");
            Widgets.EndScrollView();
        }

        // Filter + sort (ending soonest first). Built only on change, not per frame.
        private static List<WantDto> Build(List<WantDto> src, string flt, bool mineOnly, string me)
        {
            List<WantDto> outList = new List<WantDto>();
            if (src == null) return outList;
            foreach (WantDto w in src)
            {
                if (w == null) continue;
                if (mineOnly && !Eq(w.BuyerUsername, me)) continue;
                if (flt.Length > 0)
                {
                    string item = (ItemLabels.ResolveLabel(w.ItemDefName) ?? "").ToLowerInvariant();
                    string buy  = (w.BuyerUsername ?? "").ToLowerInvariant();
                    if (!item.Contains(flt) && !buy.Contains(flt) && w.Id.ToString() != flt) continue;
                }
                outList.Add(w);
            }
            outList.Sort((x, z) => x.EndsUtcTicks.CompareTo(z.EndsUtcTicks));
            return outList;
        }

        private static bool Eq(string x, string y) => !string.IsNullOrEmpty(x) && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

        private static void DrawRow(Rect row, WantDto w, string me, long now)
        {
            Rect inner = row.ContractedBy(6f);
            const float reservedRight = 116f;
            float textW = inner.width - reservedRight;

            bool mine      = Eq(w.BuyerUsername, me);
            int  remaining = Math.Max(0, w.QtyWanted - w.QtyFilled);
            string label   = ItemLabels.ResolveLabel(w.ItemDefName);

            const float iconSize = 22f;
            ItemLabels.DrawIcon(new Rect(inner.x, inner.y, iconSize, iconSize), w.ItemDefName);

            // Line 1: item + qty progress + buyer
            DialogLayout.LabelTrunc(new Rect(inner.x + iconSize + 6f, inner.y, textW - iconSize - 6f, 18f),
                $"<b>#{w.Id}  {label}</b>  <color=grey>·</color> {w.QtyFilled}/{w.QtyWanted} filled  <color=grey>· wanted by</color> {Buyer(w.BuyerUsername)}");

            // Line 2: unit price + total escrow
            Color old = GUI.color; GUI.color = new Color(0.85f, 0.85f, 0.85f);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 20f, textW, 18f),
                $"Pays <color=yellow>{SilverFmt.Format(w.UnitPriceSilver)}</color> each  <color=grey>· {SilverFmt.Format(w.EscrowRemaining)} left in escrow</color>");
            GUI.color = old;

            // Line 3: time left
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 38f, textW, 18f),
                $"ends in {DialogLayout.TimeLeft(w.EndsUtcTicks, now)}");
            GUI.color = old;

            // Actions
            float bw = 106f, bx = inner.xMax - bw;
            if (mine)
            {
                if (IconButton.Draw(new Rect(bx, inner.y + 4f, bw, 24f), KMHTextures.Cancel, "Cancel"))
                    WantHandler.TryCancel(w.Id);
            }
            else if (remaining > 0)
            {
                if (IconButton.Draw(new Rect(bx, inner.y + 4f, bw, 24f), KMHTextures.Post, "Fulfill"))
                    OpenFulfill(w, remaining);
            }
        }

        // Fulfillment delivers from the player's KMH treasury, so cap the amount at what they actually hold there.
        private static void OpenFulfill(WantDto w, int remaining)
        {
            int have = 0;
            Treasury.Dto.TreasurySnapshot snap = Treasury.TreasuryCache.Snapshot;
            if (snap?.Items != null) snap.Items.TryGetValue(w.ItemDefName, out have);
            if (have <= 0)
            {
                Notifications.KmhNotifications.Rejected($"Deposit {ItemLabels.ResolveLabel(w.ItemDefName)} to your treasury first to fulfill this want.");
                return;
            }
            int max = Math.Min(remaining, have);
            long id = w.Id;
            Find.WindowStack.Add(new Dialog_KMHAmountInput(
                title:        $"Fulfill: deliver {ItemLabels.ResolveLabel(w.ItemDefName)}",
                confirmLabel: "Deliver",
                unitLabel:    $"units (you have {have}, want needs {remaining})",
                maxHint:      max,
                onConfirm:    qty => WantHandler.TryFulfill(id, qty)));
        }

        private static string Buyer(string u)
            => LinkedAccountsCache.Format(u);   // Format() already renders empty as "(unknown)"
    }
}
