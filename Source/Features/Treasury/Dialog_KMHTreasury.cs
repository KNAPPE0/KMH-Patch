using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Treasury.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Treasury
{
    // Treasury vault browser: two-pane (items left, recent activity right), a permission banner, and a lifetime
    // in/out display.
    //
    // Deposit / Withdraw flows are fully wired via OpenDepositMenu / OpenWithdrawMenu float menus →
    // Dialog_KMHAmountInput (silver) or Dialog_KMHItemPicker (items). The recent-activity panel renders the last N
    // transactions inline, so no separate logs page is needed.
    public class Dialog_KMHTreasury : Window_KMHBase
    {

        public override Vector2 InitialSize => new Vector2(980f, 580f);

        private Vector2 _itemScroll;
        private Vector2 _txScroll;

        // Materialized vault items, cached per snapshot so a big modded vault isn't copied every frame.
        private System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> _itemsView;
        private object _itemsSource;

        // Auto-refresh on an 8s cadence. Server pushes unsolicited snapshots on mutations too, so this is the
        // failover.
        private float    _refreshTimer  = DialogLayout.AutoRefreshSeconds;
        private DateTime _lastRefreshUtc = DateTime.UtcNow;

        public Dialog_KMHTreasury()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            TreasuryHandler.RequestSnapshot();
            _lastRefreshUtc = DateTime.UtcNow;
            TreasuryCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            TreasuryCache.Updated -= OnSnapshotUpdated;
        }

        private void OnSnapshotUpdated() => _lastRefreshUtc = DateTime.UtcNow;

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                TreasuryHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            TreasurySnapshot s = TreasuryCache.Snapshot;
            string headerTitle = s == null
                ? "Treasury"
                : (s.IsGuildOwned ? $"Guild Treasury - {DisplayOwner(s.OwnerKey)}" : "Personal Vault");

            float y = DialogLayout.DrawTitle(rect, headerTitle);

            int secsSince = Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds);
            DialogLayout.DrawLiveBadge(rect, secsSince);

            if (!TreasuryCache.HasSnapshot)
            {
                DialogLayout.LabelTrunc(new Rect(0f, 40f, rect.width, 20f),
                    "<color=grey>Loading treasury…</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            DialogLayout.DrawSectionDivider(rect, ref y);

            // Top row: silver + lifetime stats.
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f),
                $"<b>Silver:</b> {s.SilverBalance}    " +
                $"<color=grey>(in: {s.LifetimeSilverIn} | out: {s.LifetimeSilverOut})</color>");
            y += 28f;

            // Permission banner. Empty/Read-only when neither deposit nor withdraw is allowed.
            string perm = "";
            if (s.CanDeposit)  perm += "Deposit ";
            if (s.CanWithdraw) perm += "Withdraw ";
            if (string.IsNullOrEmpty(perm)) perm = "Read-only";

            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f), $"Permissions: {perm.Trim()}");
            GUI.color = oldCol;
            y += 24f;

            // 50/50 split.
            float paneH  = rect.height - y - 80f;
            float leftW  = rect.width * 0.5f - 4f;
            float rightX = leftW + 8f;
            float rightW = rect.width - rightX;

            DialogLayout.LabelTrunc(new Rect(0f, y, leftW, 20f), "<b>Items</b>");
            Rect itemsBox = new Rect(0f, y + 22f, leftW, paneH - 22f);
            Widgets.DrawMenuSection(itemsBox);
            DrawItemsList(itemsBox, s);

            DialogLayout.LabelTrunc(new Rect(rightX, y, rightW, 20f), "<b>Recent Activity</b>");
            Rect txBox = new Rect(rightX, y + 22f, rightW, paneH - 22f);
            Widgets.DrawMenuSection(txBox);
            DrawTransactionsList(txBox, s);

            // Button row. Both Deposit and Withdraw float-menu helpers are fully implemented (silver / item flows
            // below); buttons are gated on the per-caller CanDeposit / CanWithdraw permission flags the server
            // stamps on the snapshot
            const float btnH = 32f;
            const float btnW = 140f;
            float btnY = rect.height - 40f;
            float bx   = 0f;

            if (s.CanDeposit)
            {
                if (IconButton.Draw(new Rect(bx, btnY, btnW, btnH), KMHTextures.Deposit, "Deposit ▾"))
                {
                    OpenDepositMenu(s);
                }
                bx += btnW + 8f;
            }
            if (s.CanWithdraw)
            {
                if (IconButton.Draw(new Rect(bx, btnY, btnW, btnH), KMHTextures.Withdraw, "Withdraw ▾"))
                {
                    OpenWithdrawMenu(s);
                }
                bx += btnW + 8f;
            }

            // Refresh on the right, close on the far right.
            if (Widgets.ButtonText(new Rect(rect.width - btnW * 2f - 8f, btnY, btnW, btnH), "Refresh"))
            {
                TreasuryHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
            if (Widgets.ButtonText(new Rect(rect.width - btnW, btnY, btnW, btnH), "Close")) Close();
        }

        private void DrawItemsList(Rect box, TreasurySnapshot s)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 28f;

            object src = s.Items;
            if (_itemsView == null || !ReferenceEquals(_itemsSource, src))
            {
                _itemsView = s.Items != null
                    ? new List<KeyValuePair<string, int>>(s.Items)
                    : new List<KeyValuePair<string, int>>();
                _itemsSource = src;
            }
            List<KeyValuePair<string, int>> items = _itemsView;
            int count = items.Count;

            float viewH = Mathf.Max(inner.height, count * rowH + 4f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _itemScroll, viewRect);
            DialogLayout.VisibleRange(_itemScroll, inner.height, rowH, count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                KeyValuePair<string, int> kv = items[i];
                float ly = i * rowH;
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                const float iconSize = 24f;
                ItemLabels.DrawIcon(new Rect(4f, ly + 2f, iconSize, iconSize), kv.Key);
                string label = ItemLabels.ResolveLabel(kv.Key);
                DialogLayout.LabelTrunc(new Rect(4f + iconSize + 6f, ly + 4f, viewRect.width - iconSize - 18f, rowH - 8f),
                    $"{label}   <color=grey>x{kv.Value}</color>");
            }
            if (count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f),
                    "<color=grey>No items in vault.</color>");
            }
            Widgets.EndScrollView();
        }

        private void DrawTransactionsList(Rect box, TreasurySnapshot s)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 42f;

            // Recent transactions arrive oldest-first; we reverse for display so newest is on top, like an activity
            // feed.
            List<TreasuryTransaction> txs = s.RecentTransactions ?? new List<TreasuryTransaction>();
            int count = txs.Count;

            float viewH = Mathf.Max(inner.height, count * rowH + 4f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _txScroll, viewRect);
            float ly = 0f;

            for (int i = count - 1, idx = 0; i >= 0; i--, idx++)
            {
                TreasuryTransaction tx = txs[i];
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (idx % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                // Top line: colored kind chip + actor on left, amount/item on right.
                Color chipCol = ColorForKind(tx.Kind);
                string chipText = FriendlyKind(tx.Kind);

                Color oldCol = GUI.color;
                GUI.color = chipCol;
                DialogLayout.LabelTrunc(new Rect(6f, ly + 2f, 130f, 20f), $"<b>{chipText}</b>");
                GUI.color = oldCol;
                DialogLayout.LabelTrunc(new Rect(140f, ly + 2f, viewRect.width - 280f, 20f),
                    string.IsNullOrEmpty(tx.Username) ? "<color=grey>-</color>" : LinkedAccountsCache.Format(tx.Username));

                string amountText = string.IsNullOrEmpty(tx.ItemDefName)
                    ? $"{tx.Amount}s"
                    : $"×{tx.Amount} {ItemLabels.ResolveLabel(tx.ItemDefName)}";
                Text.Anchor = TextAnchor.UpperRight;
                DialogLayout.LabelTrunc(new Rect(viewRect.width - 200f, ly + 2f, 196f, 20f), amountText);
                Text.Anchor = TextAnchor.UpperLeft;

                // Bottom line: muted note (wraps if long).
                if (!string.IsNullOrEmpty(tx.Note))
                {
                    GUI.color = DialogLayout.MutedColor;
                    DialogLayout.LabelTrunc(new Rect(6f, ly + 22f, viewRect.width - 12f, 18f), tx.Note);
                    GUI.color = oldCol;
                }

                ly += rowH;
            }
            if (count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f),
                    "<color=grey>No recent activity.</color>");
            }
            Widgets.EndScrollView();
        }

        // Deposit item picker. Sources from the selected caravan if there is one, otherwise straight from the
        // colony's stockpiles - no caravan required.
        private static void OpenDepositItemPicker(string title, string pickActionLabel, Action<string, int> onPick)
        {
            RimWorld.Planet.Caravan caravan = CaravanReader.GetSelectedCaravan();
            Dictionary<string, int> items;
            string sourceLabel;
            if (caravan != null)
            {
                items       = CaravanReader.ReadInventory(caravan);
                sourceLabel = caravan.Label;
            }
            else
            {
                Verse.Map map = ColonyGoods.DepositMap();
                if (map == null)
                {
                    Notifications.KmhNotifications.Rejected("No colony or caravan to deposit from");
                    return;
                }
                items       = ColonyGoods.ReadStoredInventory(map);
                sourceLabel = map.Parent?.LabelCap ?? "your colony";
            }
            Find.WindowStack.Add(new Dialog_KMHItemPicker(
                title:           $"{title} - from {sourceLabel}",
                pickActionLabel: pickActionLabel,
                source:          items,
                onPick:          onPick));
        }

        // Float menus: silver opens Dialog_KMHAmountInput, items opens Dialog_KMHItemPicker (caravan source for
        // deposit, treasury source
        // for withdraw).
        private void OpenDepositMenu(TreasurySnapshot s)
        {
            // Source is the selected caravan, or the colony's stockpiles when none is selected. Read the available
            // silver once so the "all" shortcut shows the real number and the amount dialog can cap to it.
            RimWorld.Planet.Caravan caravan = CaravanReader.GetSelectedCaravan();
            int availSilver = caravan != null
                ? ColonyGoods.CountSilver(caravan)
                : ColonyGoods.CountSilverOnMap(ColonyGoods.DepositMap());

            List<FloatMenuOption> opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Deposit silver…", () =>
                {
                    // Cap the input at the available silver so a player can't even type more than they have
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title: "Deposit silver",
                        confirmLabel: "Deposit",
                        unitLabel: "silver",
                        maxHint: availSilver,
                        onConfirm: amount => TreasuryHandler.TryDepositSilver(amount)));
                })
            };

            // One-click "all" - the handler re-verifies the source, so a stale count just gets rejected.
            if (availSilver > 0)
                opts.Add(new FloatMenuOption($"Deposit all silver ({availSilver})",
                    () => TreasuryHandler.TryDepositSilver(availSilver)));

            opts.Add(new FloatMenuOption("Deposit items…", () =>
            {
                OpenDepositItemPicker(
                    title:           "Deposit items",
                    pickActionLabel: "Deposit",
                    onPick:          (defName, qty) => TreasuryHandler.TryDepositItem(defName, qty));
            }));

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void OpenWithdrawMenu(TreasurySnapshot s)
        {
            List<FloatMenuOption> opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Withdraw silver…", () =>
                {
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title: "Withdraw silver",
                        confirmLabel: "Withdraw",
                        unitLabel: "silver",
                        maxHint: s.SilverBalance,                      // we DO know vault balance
                        onConfirm: amount => TreasuryHandler.TryWithdrawSilver(amount)));
                })
            };

            // One-click "all" - the server caps to the live balance, so a stale snapshot can't over-withdraw.
            if (s.SilverBalance > 0)
                opts.Add(new FloatMenuOption($"Withdraw all silver ({s.SilverBalance})",
                    () => TreasuryHandler.TryWithdrawSilver(s.SilverBalance)));

            opts.Add(new FloatMenuOption("Withdraw items…", () =>
                {
                    // Source is the treasury snapshot we already have cached
                    // - no caravan dependency for withdraws.
                    Find.WindowStack.Add(new Dialog_KMHItemPicker(
                        title:           $"Withdraw from {(s.IsGuildOwned ? "Guild Treasury" : "Personal Vault")}",
                        pickActionLabel: "Withdraw",
                        source:          s.Items ?? new Dictionary<string, int>(),
                        onPick:          (defName, qty) => TreasuryHandler.TryWithdrawItem(defName, qty)));
                }));

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Maps transaction kind string to a color matching the action's tone. (Deposit = inflow green; Withdraw =
        // outflow amber; Marketplace tax = red; etc.)
        private static Color ColorForKind(string kind)
        {
            switch (kind)
            {
                case TreasuryTransaction.KindDeposit:           return new Color(0.5f, 0.95f, 0.5f);
                case TreasuryTransaction.KindWithdraw:          return new Color(1.0f, 0.75f, 0.4f);
                case TreasuryTransaction.KindSiteRewardSilver:
                case TreasuryTransaction.KindSiteRewardItem:    return new Color(1.0f, 0.95f, 0.55f);
                case TreasuryTransaction.KindMarketplaceSale:   return new Color(0.55f, 0.85f, 1.0f);
                case TreasuryTransaction.KindMarketplaceTax:    return new Color(1.0f, 0.55f, 0.55f);
                case TreasuryTransaction.KindMarketplaceRefund: return new Color(0.8f,  0.8f,  0.8f);
                default:                                        return Color.white;
            }
        }

        private static string FriendlyKind(string kind)
        {
            switch (kind)
            {
                case TreasuryTransaction.KindDeposit:           return "Deposit";
                case TreasuryTransaction.KindWithdraw:          return "Withdraw";
                case TreasuryTransaction.KindSiteRewardSilver:  return "Site Reward";
                case TreasuryTransaction.KindSiteRewardItem:    return "Site Reward";
                case TreasuryTransaction.KindMarketplaceSale:   return "Sale";
                case TreasuryTransaction.KindMarketplaceTax:    return "Market Tax";
                case TreasuryTransaction.KindMarketplaceRefund: return "Refund";
                default:                                        return kind ?? "Unknown";
            }
        }

        // OwnerKey for personal vaults is "_personal:<username>" - strip the prefix for the title so it just shows
        // the username
        private static string DisplayOwner(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey)) return "(unknown)";
            const string personalPrefix = "_personal:";
            if (ownerKey.StartsWith(personalPrefix)) return ownerKey.Substring(personalPrefix.Length);
            return ownerKey;
        }
    }
}
