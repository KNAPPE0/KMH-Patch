using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Treasury.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Treasury
{
    // Treasury vault browser: items + recent activity, deposit/withdraw via float menus.
    public class Dialog_KMHTreasury : Window_KMHBase
    {

        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(980f, 580f);

        private Vector2 _itemScroll;
        private Vector2 _txScroll;

        // Cached per snapshot so a big modded vault isn't rebuilt every frame.
        private List<VaultRow> _itemsView;
        private object _itemsSource;
        private object _payloadSource;

        private struct VaultRow
        {
            public string Key;
            public int Count;
            public KMHPatch.Items.KmhThingPayload Payload;   // set for full-state stacks; Key/Count unused then
        }

        public Dialog_KMHTreasury()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            // Auto-refresh failover; the server also pushes unsolicited snapshots on mutations.
            EnableAutoRefresh(() => TreasuryHandler.RequestSnapshot());
            TreasuryCache.Updated += MarkRefreshed;
        }

        public override void PostClose()
        {
            base.PostClose();
            TreasuryCache.Updated -= MarkRefreshed;
        }

        protected override void DrawContents(Rect rect)
        {
            TreasurySnapshot s = TreasuryCache.Snapshot;
            string headerTitle = s == null
                ? "Treasury"
                : (s.IsGuildOwned ? $"Guild Treasury - {DisplayOwner(s.OwnerKey)}" : "Personal Vault");

            float y = DialogLayout.DrawTitle(rect, headerTitle);

            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);

            if (!TreasuryCache.HasSnapshot)
            {
                // A request that never came back used to sit on "Loading…" while the badge claimed the screen was live.
                DialogLayout.LabelTrunc(new Rect(0f, 40f, rect.width, 20f), WaitedTooLong
                    ? "<color=#ffcf59>No response from the server yet.</color> <color=grey>Your treasury may be too large for one message, or the connection dropped. Try Refresh.</color>"
                    : "<color=grey>Loading treasury…</color>");
                if (Widgets.ButtonText(new Rect(0f, 66f, 120f, 24f), "Refresh"))
                    TreasuryHandler.RequestSnapshot();
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            DialogLayout.DrawSectionDivider(rect, ref y);

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f),
                $"<b>Silver:</b> {s.SilverBalance}    " +
                $"<color=grey>(in: {s.LifetimeSilverIn} | out: {s.LifetimeSilverOut})</color>");
            y += 28f;

            // Pending deposits: held until the local save is durable (disconnect/rollback dupe guard). Not spendable.
            if (s.PendingDeposits != null && s.PendingDeposits.Count > 0)
            {
                int pendSilver = 0, pendItems = 0;
                foreach (Dto.PendingDeposit p in s.PendingDeposits) { if (p == null) continue; pendSilver += p.Silver; pendItems += p.Qty; }
                string parts = pendSilver > 0 && pendItems > 0 ? $"{pendSilver} silver + {pendItems} item(s)"
                             : pendSilver > 0 ? $"{pendSilver} silver" : $"{pendItems} item(s)";
                Color pc = GUI.color; GUI.color = new Color(0.95f, 0.8f, 0.35f);
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                    $"⏳ Pending: {parts} - save your game to finalize (not spendable yet).");
                GUI.color = pc;
                y += 22f;
            }

            string perm = "";
            if (s.CanDeposit)  perm += "Deposit ";
            if (s.CanWithdraw) perm += "Withdraw ";
            if (string.IsNullOrEmpty(perm)) perm = "Read-only";

            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f), $"Permissions: {perm.Trim()}");
            GUI.color = oldCol;
            y += 24f;

            // Floored: the window is resizeable and BeginScrollView throws on a negative rect.
            float paneH  = Mathf.Max(DialogLayout.MinBodyHeight + 22f, rect.height - y - 80f);
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

            const float btnH = 32f;
            float btnY = rect.height - 40f;
            float bx   = 0f;

            // Shrink together: at a flat 140px the Refresh/Close pair overlapped Deposit/Withdraw under ~600px wide.
            float btnW = Mathf.Clamp((rect.width - 24f) / 4f, 74f, 140f);

            if (s.CanDeposit)
            {
                if (IconButton.Draw(new Rect(bx, btnY, btnW, btnH), KMHTextures.Deposit, "Deposit ▾"))
                {
                    OpenDepositMenu();
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

            if (Widgets.ButtonText(new Rect(rect.width - btnW * 2f - 8f, btnY, btnW, btnH), "Refresh"))
            {
                TreasuryHandler.RequestSnapshot();
            }
            if (Widgets.ButtonText(new Rect(rect.width - btnW, btnY, btnW, btnH), "Close")) Close();
        }

        private void DrawItemsList(Rect box, TreasurySnapshot s)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 28f;

            // Everything in the vault, in one list: plain stacks + full-state stacks (with their quality/hp state).
            if (_itemsView == null || !ReferenceEquals(_itemsSource, s.Items) || !ReferenceEquals(_payloadSource, s.ItemPayloads))
            {
                _itemsView = new List<VaultRow>();
                if (s.Items != null)
                    foreach (KeyValuePair<string, int> kv in s.Items)
                        _itemsView.Add(new VaultRow { Key = kv.Key, Count = kv.Value });
                if (s.ItemPayloads != null)
                    foreach (KMHPatch.Items.KmhThingPayload p in s.ItemPayloads)
                        if (p != null && p.StackCount > 0) _itemsView.Add(new VaultRow { Payload = p });
                _itemsSource = s.Items;
                _payloadSource = s.ItemPayloads;
            }
            List<VaultRow> items = _itemsView;
            int count = items.Count;

            float viewH = Mathf.Max(inner.height, count * rowH + 4f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _itemScroll, viewRect);
            DialogLayout.VisibleRange(_itemScroll, inner.height, rowH, count, out int first, out int last);
            for (int i = first; i < last; i++)
            {
                VaultRow vr = items[i];
                float ly = i * rowH;
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                const float iconSize = 24f;
                string iconKey = vr.Payload != null ? vr.Payload.DefName : vr.Key;
                ItemLabels.DrawIcon(new Rect(4f, ly + 2f, iconSize, iconSize), iconKey);
                float textX = 4f + iconSize + UI.KmhItemInfo.Size + 8f;
                Rect textRect = new Rect(textX, ly + 4f, viewRect.width - iconSize - UI.KmhItemInfo.Size - 22f, rowH - 8f);

                if (vr.Payload != null)
                {
                    // Info card with the exact def+stuff so the stack can be double-checked before withdrawing.
                    UI.KmhItemInfo.ButtonForDef(4f + iconSize + 4f, ly + 2f, vr.Payload.DefName, vr.Payload.StuffDefName);
                    DialogLayout.LabelTrunc(textRect,
                        $"{UI.KmhItemRow.PayloadLabel(vr.Payload)}{UI.KmhItemRow.PayloadSuffix(vr.Payload)}   <color=grey>x{vr.Payload.StackCount}</color>");
                }
                else
                {
                    UI.KmhItemInfo.ButtonForKey(4f + iconSize + 4f, ly + 2f, vr.Key);
                    DialogLayout.LabelTrunc(textRect,
                        $"{ItemLabels.ResolveLabel(vr.Key)}   <color=grey>x{vr.Count}</color>");
                }
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
            // Two text lines. At 42 with a 22px pitch the note overlapped the line above it and ran past the row.
            float rowH = DialogLayout.TextRowsH(2, 6f);

            // Transactions arrive oldest-first; drawn in reverse so newest is on top.
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

                const float amountW = 196f;
                Color chipCol = ColorForKind(tx.Kind);
                string chipText = FriendlyKind(tx.Kind);

                Color oldCol = GUI.color;
                GUI.color = chipCol;
                DialogLayout.LabelTrunc(new Rect(6f, ly + 2f, 130f, DialogLayout.TextRowH), $"<b>{chipText}</b>");
                GUI.color = oldCol;
                // Derived from amountW - a fixed actor width overlapped the amount column at every window size.
                float actorW = Mathf.Max(0f, viewRect.width - amountW - 8f - 140f);
                DialogLayout.LabelTrunc(new Rect(140f, ly + 2f, actorW, DialogLayout.TextRowH),
                    string.IsNullOrEmpty(tx.Username) ? "<color=grey>-</color>" : LinkedAccountsCache.Format(tx.Username));

                string amountText = string.IsNullOrEmpty(tx.ItemDefName)
                    ? SilverFmt.Format(tx.Amount)
                    : DescribeTxItem(tx);
                Text.Anchor = TextAnchor.UpperRight;
                DialogLayout.LabelTrunc(new Rect(viewRect.width - amountW - 4f, ly + 2f, amountW, DialogLayout.TextRowH), amountText);
                Text.Anchor = TextAnchor.UpperLeft;

                if (!string.IsNullOrEmpty(tx.Note))
                {
                    GUI.color = DialogLayout.MutedColor;
                    DialogLayout.LabelTrunc(new Rect(6f, ly + 2f + DialogLayout.TextRowH, viewRect.width - 12f, DialogLayout.TextRowH), tx.Note);
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

        // Re-reads live counts after each pick, so the picker never offers stock the colony no longer holds.
        private static void OpenDepositItemPicker(string title, string pickActionLabel, Action<string, int> onPick)
        {
            Dictionary<string, int> items = ReadDepositSource(out string sourceLabel);
            if (items == null)
            {
                Notifications.KmhNotifications.Rejected("No colony or caravan to deposit from");
                return;
            }
            UI.KmhItemPickerService.Open(
                title:           $"{title} - from {sourceLabel}",
                pickActionLabel: pickActionLabel,
                source:          items,
                onPick:          onPick,
                refreshSource:   () => ReadDepositSource(out _));
        }

        // Live snapshot of what can be deposited right now (selected caravan, else the colony's stockpiles).
        private static Dictionary<string, int> ReadDepositSource(out string sourceLabel)
        {
            RimWorld.Planet.Caravan caravan = CaravanReader.GetSelectedCaravan();
            if (caravan != null)
            {
                sourceLabel = caravan.Label;
                return CaravanReader.ReadInventory(caravan);
            }
            Verse.Map map = ColonyGoods.DepositMap();
            if (map == null) { sourceLabel = ""; return null; }
            sourceLabel = map.Parent?.LabelCap ?? "your colony";
            return ColonyGoods.ReadStoredInventory(map);
        }

        // Takes no snapshot on purpose: what you can deposit is colony/caravan stock, read live below.
        private void OpenDepositMenu()
        {
            // Read once so the "all" shortcut and the amount dialog's cap agree on the same number.
            RimWorld.Planet.Caravan caravan = CaravanReader.GetSelectedCaravan();
            int availSilver = caravan != null
                ? ColonyGoods.CountSilver(caravan)
                : ColonyGoods.CountSilverOnMap(ColonyGoods.DepositMap());

            List<FloatMenuOption> opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Deposit silver", () =>
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

            opts.Add(new FloatMenuOption("Deposit items", () =>
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
                new FloatMenuOption("Withdraw silver", () =>
                {
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title: "Withdraw silver",
                        confirmLabel: "Withdraw",
                        unitLabel: "silver",
                        maxHint: s.SilverBalance,
                        onConfirm: amount => TreasuryHandler.TryWithdrawSilver(amount)));
                })
            };

            // One-click "all" - the server caps to the live balance, so a stale snapshot can't over-withdraw.
            if (s.SilverBalance > 0)
                opts.Add(new FloatMenuOption($"Withdraw all silver ({s.SilverBalance})",
                    () => TreasuryHandler.TryWithdrawSilver(s.SilverBalance)));

            // One entry for the whole vault: listing items individually here overflows the screen on a big vault.
            bool hasPayloads = false;
            if (s.ItemPayloads != null)
                foreach (KMHPatch.Items.KmhThingPayload p in s.ItemPayloads)
                    if (p != null && p.StackCount > 0) { hasPayloads = true; break; }

            if ((s.Items != null && s.Items.Count > 0) || hasPayloads)
                opts.Add(new FloatMenuOption("Withdraw items", () =>
                {
                    TreasurySnapshot live = TreasuryCache.Snapshot ?? s;
                    UI.KmhItemPickerService.Open(
                        title:           $"Withdraw from {(s.IsGuildOwned ? "Guild Treasury" : "Personal Vault")}",
                        pickActionLabel: "Withdraw",
                        source:          live.Items ?? new Dictionary<string, int>(),
                        onPick:          (defName, qty) => TreasuryHandler.TryWithdrawItem(defName, qty),
                        refreshSource:   () => TreasuryCache.Snapshot?.Items,
                        payloads:        live.ItemPayloads,
                        onPickPayload:   (pl, qty) => TreasuryHandler.TryWithdrawPayload(pl.Fingerprint, qty),
                        refreshPayloads: () => TreasuryCache.Snapshot?.ItemPayloads);
                }));

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Payload moves arrive already counted ("75x Wood plank"); prefixing those would render "×75 75x Wood plank".
        private static string DescribeTxItem(TreasuryTransaction tx)
        {
            string item = tx.ItemDefName ?? "";
            return LeadsWithCount(item) ? item : $"×{tx.Amount} {ItemLabels.ResolveLabel(item)}";
        }

        private static bool LeadsWithCount(string s)
        {
            int i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            return i > 0 && i + 1 < s.Length && (s[i] == 'x' || s[i] == 'X') && s[i + 1] == ' ';
        }

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

        private static string DisplayOwner(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey)) return "(unknown)";
            const string personalPrefix = "_personal:";
            if (ownerKey.StartsWith(personalPrefix)) return ownerKey.Substring(personalPrefix.Length);
            return ownerKey;
        }
    }
}
