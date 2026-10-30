using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Treasury.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Treasury
{
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
            public KMHPatch.Items.KmhThingPayload Payload;
        }

        public Dialog_KMHTreasury()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            // The only charged call; auto-refresh uses a different kind so an open window is never billed by the clock.
            TreasuryHandler.OpenAccess();

            // Auto-refresh failover; the server also pushes unsolicited snapshots on mutations.
            EnableAutoRefresh(() => TreasuryHandler.RequestSnapshot());
            TreasuryCache.Updated += MarkRefreshed;
        }

        public override void PostClose()
        {
            base.PostClose();
            TreasuryCache.Updated -= MarkRefreshed;
        }

        protected override Features.Discord.KmhActivity PresenceActivity => Features.Discord.KmhActivity.Treasury;

        protected override void DrawContents(Rect rect)
        {
            TreasurySnapshot s = TreasuryCache.Snapshot;
            string headerTitle = s == null
                ? KmhText.Get("KMH_Tab_Treasury", "Treasury")
                : (s.IsGuildOwned ? KmhText.Get("KMH_Treasury_GuildTitle", "Guild Treasury - {0}", DisplayOwner(s.OwnerKey))
                                  : KmhText.Get("KMH_Treasury_PersonalVault", "Personal Vault"));

            float y = DialogLayout.DrawTitle(rect, headerTitle, DialogLayout.LiveBadgeReserve);

            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);

            string refreshLabel = KmhText.Get("KMH_GH_Refresh", "Refresh");

            if (!TreasuryCache.HasSnapshot)
            {
                DialogLayout.LabelTrunc(new Rect(0f, 40f, rect.width, 20f), WaitedTooLong
                    ? "<color=#f0c04f>" + KmhText.Get("KMH_Treasury_NoResponse", "No response from the server yet.") + "</color> <color=#A9AFB9>"
                      + KmhText.Get("KMH_Treasury_NoResponseWhy", "Your treasury may be too large for one message, or the connection dropped. Try Refresh.")
                      + "</color>"
                    : "<color=#A9AFB9>" + KmhText.Get("KMH_Treasury_Loading", "Loading treasury…") + "</color>");
                if (Widgets.ButtonText(new Rect(0f, 66f, 120f, 24f), refreshLabel))
                    TreasuryHandler.RequestSnapshot();
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            DialogLayout.DrawSectionDivider(rect, ref y);

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f),
                "<b>" + KmhText.Get("KMH_Treasury_Silver", "Silver:") + "</b> " + s.SilverBalance + "    <color=#A9AFB9>"
                + KmhText.Get("KMH_Treasury_InOut", "(in: {0} | out: {1})", s.LifetimeSilverIn, s.LifetimeSilverOut) + "</color>");
            y += 28f;

            // Pending deposits: held until the local save is durable (disconnect/rollback dupe guard). Not spendable.
            if (s.PendingDeposits != null && s.PendingDeposits.Count > 0)
            {
                int pendSilver = 0, pendItems = 0;
                foreach (Dto.PendingDeposit p in s.PendingDeposits) { if (p == null) continue; pendSilver += p.Silver; pendItems += p.Qty; }
                string parts = pendSilver > 0 && pendItems > 0 ? SilverAmount(pendSilver) + " + " + ItemCount(pendItems)
                             : pendSilver > 0 ? SilverAmount(pendSilver) : ItemCount(pendItems);
                // RimWorld Together's own save finalizes pending deposits in place, without quitting.
                bool canSave = KmhSaveNow.Available;
                float saveW = canSave ? 120f : 0f;
                Color pc = GUI.color; GUI.color = new Color(0.95f, 0.8f, 0.35f);
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width - saveW - (canSave ? 8f : 0f), 20f),
                    KmhText.Get("KMH_Treasury_Pending", "Pending: {0} - save your game to finalize (not spendable yet).", parts));
                GUI.color = pc;
                if (canSave)
                {
                    Rect saveRect = new Rect(rect.width - saveW, y - 2f, saveW, 24f);
                    bool busy = KmhSaveNow.Busy;
                    string label = busy ? KmhText.Get("KMH_Treasury_Saving", "Saved") : KmhText.Get("KMH_Treasury_SaveNow", "Save now");
                    if (Widgets.ButtonText(saveRect, label, active: !busy) && !busy) KmhSaveNow.TrySave();
                    TooltipHandler.TipRegion(saveRect, KmhText.Get("KMH_Treasury_SaveNowTip",
                        "Saves your colony the way RimWorld Together does, without quitting, so pending deposits become spendable."));
                    y += 4f;
                }
                y += 22f;
            }

            string limit = s.CanDeposit && s.CanWithdraw ? null
                         : s.CanDeposit  ? KmhText.Get("KMH_Treasury_RankDepositOnly", "Your rank can deposit here, but not withdraw.")
                         : s.CanWithdraw ? KmhText.Get("KMH_Treasury_RankWithdrawOnly", "Your rank can withdraw here, but not deposit.")
                         :                 KmhText.Get("KMH_Treasury_RankReadOnly", "Read-only for your rank.");
            if (limit != null)
            {
                Color oldCol = GUI.color;
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f), limit);
                GUI.color = oldCol;
                y += 24f;
            }

            // Floored: the window is resizeable and BeginScrollView throws on a negative rect.
            float paneH  = Mathf.Max(DialogLayout.MinBodyHeight + 22f, rect.height - y - 80f);
            float leftW  = rect.width * 0.5f - 4f;
            float rightX = leftW + 8f;
            float rightW = rect.width - rightX;

            DialogLayout.LabelTrunc(new Rect(0f, y, leftW, 20f), "<b>" + KmhText.Get("KMH_Treasury_Items", "Items") + "</b>");
            Rect itemsBox = new Rect(0f, y + 22f, leftW, paneH - 22f);
            Widgets.DrawMenuSection(itemsBox);
            DrawItemsList(itemsBox, s);

            DialogLayout.LabelTrunc(new Rect(rightX, y, rightW, 20f),
                "<b>" + KmhText.Get("KMH_Treasury_RecentActivity", "Recent Activity") + "</b>");
            Rect txBox = new Rect(rightX, y + 22f, rightW, paneH - 22f);
            Widgets.DrawMenuSection(txBox);
            DrawTransactionsList(txBox, s);

            const float btnH = 32f;
            float btnY = rect.height - 40f;
            float bx   = 0f;

            // Shrink together: at a flat 140px the Refresh/Close pair overlaps Deposit/Withdraw under ~600px wide.
            float btnW = Mathf.Clamp((rect.width - 24f) / 4f, 74f, 140f);

            if (s.CanDeposit)
            {
                if (IconButton.Draw(new Rect(bx, btnY, btnW, btnH), KMHTextures.Deposit, KmhText.Get("KMH_Treasury_DepositMenu", "Deposit ▾")))
                {
                    OpenDepositMenu();
                }
                bx += btnW + 8f;
            }
            if (s.CanWithdraw)
            {
                if (IconButton.Draw(new Rect(bx, btnY, btnW, btnH), KMHTextures.Withdraw, KmhText.Get("KMH_Treasury_WithdrawMenu", "Withdraw ▾")))
                {
                    OpenWithdrawMenu(s);
                }
                bx += btnW + 8f;
            }

            if (Widgets.ButtonText(new Rect(rect.width - btnW * 2f - 8f, btnY, btnW, btnH), refreshLabel))
            {
                TreasuryHandler.RequestSnapshot();
            }
            if (Widgets.ButtonText(new Rect(rect.width - btnW, btnY, btnW, btnH), KmhText.Get("KMH_Common_Close", "Close"))) Close();
        }

        private void DrawItemsList(Rect box, TreasurySnapshot s)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 28f;

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
                    UI.KmhItemInfo.ButtonForDef(4f + iconSize + 4f, ly + 2f, vr.Payload.DefName, vr.Payload.StuffDefName);
                    DialogLayout.LabelTrunc(textRect,
                        $"{UI.KmhItemRow.PayloadLabel(vr.Payload)}{UI.KmhItemRow.PayloadSuffix(vr.Payload)}   <color=#A9AFB9>x{vr.Payload.StackCount}</color>");
                }
                else
                {
                    UI.KmhItemInfo.ButtonForKey(4f + iconSize + 4f, ly + 2f, vr.Key);
                    DialogLayout.LabelTrunc(textRect,
                        $"{ItemLabels.ResolveLabel(vr.Key)}   <color=#A9AFB9>x{vr.Count}</color>");
                }
            }
            if (count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_Treasury_NoItems", "No items in vault.") + "</color>");
            }
            Widgets.EndScrollView();
        }

        private void DrawTransactionsList(Rect box, TreasurySnapshot s)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            float rowH = DialogLayout.TextRowsH(2, 6f);

            // Transactions arrive oldest-first; drawn in reverse so newest is on top.
            List<TreasuryTransaction> txs = s.RecentTransactions ?? new List<TreasuryTransaction>();
            int count = txs.Count;

            float viewH = Mathf.Max(inner.height, count * rowH + 4f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _txScroll, viewRect);
            DialogLayout.VisibleRange(_txScroll, inner.height, rowH, count, out int first, out int last);

            for (int idx = first; idx < last; idx++)
            {
                TreasuryTransaction tx = txs[count - 1 - idx];
                float ly = idx * rowH;
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
                float actorW = Mathf.Max(0f, viewRect.width - amountW - 8f - 140f);
                DialogLayout.LabelTrunc(new Rect(140f, ly + 2f, actorW, DialogLayout.TextRowH),
                    string.IsNullOrEmpty(tx.Username) ? "<color=#A9AFB9>-</color>" : LinkedAccountsCache.Format(tx.Username));

                string amountText = string.IsNullOrEmpty(tx.ItemDefName)
                    ? SilverFmt.Format(tx.Amount)
                    : DescribeTxItem(tx);
                Text.Anchor = TextAnchor.UpperRight;
                DialogLayout.LabelTrunc(new Rect(viewRect.width - amountW - 4f, ly + 2f, amountW, DialogLayout.TextRowH), amountText);
                Text.Anchor = TextAnchor.UpperLeft;

                if (!string.IsNullOrEmpty(tx.Note))
                {
                    GUI.color = DialogLayout.MutedColor;
                    Rect noteRect = new Rect(6f, ly + 2f + DialogLayout.TextRowH, viewRect.width - 12f, DialogLayout.TextRowH);
                    DialogLayout.LabelTrunc(noteRect, FriendlyNote(tx.Note));
                    TooltipHandler.TipRegion(noteRect, tx.Note);
                    GUI.color = oldCol;
                }
            }
            if (count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_Treasury_NoActivity", "No recent activity.") + "</color>");
            }
            Widgets.EndScrollView();
        }

        // Re-reads live counts after each pick, so the picker never offers stock the colony no longer holds.
        private static void OpenDepositItemPicker(string title, string pickActionLabel, Action<string, int> onPick)
        {
            Dictionary<string, int> items = ReadDepositSource(out string sourceLabel);
            if (items == null)
            {
                Notifications.KmhNotifications.Rejected(KmhText.Get("KMH_Treasury_NoDepositSource", "No colony or caravan to deposit from"));
                return;
            }
            UI.KmhItemPickerService.Open(
                title:           KmhText.Get("KMH_Treasury_PickerFrom", "{0} - from {1}", title, sourceLabel),
                pickActionLabel: pickActionLabel,
                source:          items,
                onPick:          onPick,
                refreshSource:   () => ReadDepositSource(out _));
        }

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
            sourceLabel = map.Parent?.LabelCap ?? KmhText.Get("KMH_Treasury_YourColony", "your colony");
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

            string depositSilver = KmhText.Get("KMH_Treasury_DepositSilver", "Deposit silver");
            string depositItems  = KmhText.Get("KMH_Treasury_DepositItems", "Deposit items");
            List<FloatMenuOption> opts = new List<FloatMenuOption>
            {
                new FloatMenuOption(depositSilver, () =>
                {
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title: depositSilver,
                        confirmLabel: KmhText.Get("KMH_Treasury_Deposit", "Deposit"),
                        unitLabel: KmhText.Get("KMH_Treasury_UnitSilver", "silver"),
                        maxHint: availSilver,
                        onConfirm: amount => TreasuryHandler.TryDepositSilver(amount)));
                })
            };

            // One-click "all" - the handler re-verifies the source, so a stale count just gets rejected.
            if (availSilver > 0)
                opts.Add(new FloatMenuOption(KmhText.Get("KMH_Treasury_DepositAllSilver", "Deposit all silver ({0})", availSilver),
                    () => TreasuryHandler.TryDepositSilver(availSilver)));

            opts.Add(new FloatMenuOption(depositItems, () =>
            {
                OpenDepositItemPicker(
                    title:           depositItems,
                    pickActionLabel: KmhText.Get("KMH_Treasury_Deposit", "Deposit"),
                    onPick:          (defName, qty) => TreasuryHandler.TryDepositItem(defName, qty));
            }));

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void OpenWithdrawMenu(TreasurySnapshot s)
        {
            string withdrawSilver = KmhText.Get("KMH_GH_WithdrawSilver", "Withdraw silver");
            List<FloatMenuOption> opts = new List<FloatMenuOption>
            {
                new FloatMenuOption(withdrawSilver, () =>
                {
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(
                        title: withdrawSilver,
                        confirmLabel: KmhText.Get("KMH_GH_Withdraw", "Withdraw"),
                        unitLabel: KmhText.Get("KMH_Treasury_UnitSilver", "silver"),
                        maxHint: s.SilverBalance,
                        onConfirm: amount => TreasuryHandler.TryWithdrawSilver(amount)));
                })
            };

            // One-click "all" - the server caps to the live balance, so a stale snapshot can't over-withdraw.
            if (s.SilverBalance > 0)
                opts.Add(new FloatMenuOption(KmhText.Get("KMH_Treasury_WithdrawAllSilver", "Withdraw all silver ({0})", s.SilverBalance),
                    () => TreasuryHandler.TryWithdrawSilver(s.SilverBalance)));

            // One entry for the whole vault: listing items individually here overflows the screen on a big vault.
            bool hasPayloads = false;
            if (s.ItemPayloads != null)
                foreach (KMHPatch.Items.KmhThingPayload p in s.ItemPayloads)
                    if (p != null && p.StackCount > 0) { hasPayloads = true; break; }

            if ((s.Items != null && s.Items.Count > 0) || hasPayloads)
                opts.Add(new FloatMenuOption(KmhText.Get("KMH_Treasury_WithdrawItems", "Withdraw items"), () =>
                {
                    TreasurySnapshot live = TreasuryCache.Snapshot ?? s;
                    UI.KmhItemPickerService.Open(
                        title:           s.IsGuildOwned ? KmhText.Get("KMH_Treasury_WithdrawFromGuild", "Withdraw from Guild Treasury")
                                                        : KmhText.Get("KMH_Treasury_WithdrawFromPersonal", "Withdraw from Personal Vault"),
                        pickActionLabel: KmhText.Get("KMH_GH_Withdraw", "Withdraw"),
                        source:          live.Items ?? new Dictionary<string, int>(),
                        onPick:          (defName, qty) => TreasuryHandler.TryWithdrawItem(defName, qty),
                        refreshSource:   () => TreasuryCache.Snapshot?.Items,
                        payloads:        live.ItemPayloads,
                        onPickPayload:   (pl, qty) => TreasuryHandler.TryWithdrawPayload(pl.Fingerprint, qty),
                        refreshPayloads: () => TreasuryCache.Snapshot?.ItemPayloads,
                        intoThisGame:    true);
                }));

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Ledger notes name entries by their full id; the player reads the reason, and the whole note stays on hover.
        internal static string FriendlyNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return "";
            var reversed = System.Text.RegularExpressions.Regex.Match(note, @"^deposit [0-9a-f]{16,64} reversed\s*-\s*(.+)$",
                                                                      System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (reversed.Success) return KmhText.Get("KMH_Treasury_DepositReversed", "Deposit reversed - {0}", reversed.Groups[1].Value);
            return System.Text.RegularExpressions.Regex.Replace(note, @"\b[0-9a-f]{24,64}\b", m => m.Value.Substring(0, 8));
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
                case TreasuryTransaction.KindDeposit:           return KmhText.Get("KMH_Treasury_KindDeposit", "Deposit");
                case TreasuryTransaction.KindWithdraw:          return KmhText.Get("KMH_Treasury_KindWithdraw", "Withdraw");
                case TreasuryTransaction.KindSiteRewardSilver:
                case TreasuryTransaction.KindSiteRewardItem:    return KmhText.Get("KMH_Treasury_KindSiteReward", "Site Reward");
                case TreasuryTransaction.KindMarketplaceSale:   return KmhText.Get("KMH_Treasury_KindSale", "Sale");
                case TreasuryTransaction.KindMarketplaceTax:    return KmhText.Get("KMH_Treasury_KindMarketTax", "Market Tax");
                case TreasuryTransaction.KindMarketplaceRefund: return KmhText.Get("KMH_Treasury_KindRefund", "Refund");
                default:                                        return kind ?? KmhText.Get("KMH_Treasury_KindUnknown", "Unknown");
            }
        }

        private static string SilverAmount(int n) => KmhText.Get("KMH_Treasury_SilverAmount", "{0} silver", n);

        private static string ItemCount(int n)
            => n == 1 ? KmhText.Get("KMH_Treasury_ItemOne", "1 item") : KmhText.Get("KMH_Treasury_ItemMany", "{0} items", n);

        private static string DisplayOwner(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey)) return KmhText.Get("KMH_Treasury_UnknownOwner", "(unknown)");
            const string personalPrefix = "_personal:";
            if (ownerKey.StartsWith(personalPrefix)) return KmhDisplayText.Inert(ownerKey.Substring(personalPrefix.Length));
            return KmhDisplayText.GuildName(ownerKey);
        }
    }
}
