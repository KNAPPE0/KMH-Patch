using System.Linq;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Treasury.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Treasury
{
    // These move REAL goods: deposits remove locally before telling the server, withdrawals materialize only after it confirms.
    internal static class TreasuryHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.TreasurySnapshot, OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.TreasuryGrant,    OnGrant);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.TreasuryDepositApproval, OnDepositApproval);
        }

        public static bool RequestSnapshot()
            => KmhDispatcher.Send(KmhProtocol.Kind.TreasuryRequest, null);

        // Preflight first: nothing local is touched until the server approves, so a rejection can't lose goods.

        // Ties the goods-removal to the server's pending deposit; a disconnect between the two would credit goods the colony kept.
        private static string NewTxn() => System.Guid.NewGuid().ToString("N");

        private class DepositIntent { public string Kind, ItemDefName; public int Amount, Qty; public bool Complex; }
        private static readonly System.Collections.Generic.Dictionary<string, DepositIntent> _pending
            = new System.Collections.Generic.Dictionary<string, DepositIntent>();

        public static bool TryDepositSilver(int amount)
        {
            if (amount <= 0) { KmhNotifications.Rejected("Amount must be greater than 0"); return false; }
            Caravan caravan = CaravanReader.GetSelectedCaravan();
            Map     map     = caravan == null ? ColonyGoods.DepositMap() : null;
            string  src     = caravan != null ? "caravan" : "colony";
            int have = caravan != null ? ColonyGoods.CountSilver(caravan) : ColonyGoods.CountSilverOnMap(map);
            if (have < amount) { KmhNotifications.Rejected($"Your {src} only has {have} silver"); return false; }
            return SendPreflight(new DepositIntent { Kind = PendingDeposit.KindSilver, Amount = amount },
                new System.Collections.Generic.Dictionary<string, object> { { "kind", PendingDeposit.KindSilver }, { "amount", amount } },
                $"Requesting approval to deposit {amount} silver…");
        }

        public static bool TryDepositItem(string itemDefName, int qty)
        {
            if (string.IsNullOrEmpty(itemDefName)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            ItemKeys.Split(itemDefName, out string pureDef, out _, out _);
            Verse.ThingDef def = ColonyGoods.Def(pureDef);
            if (def == null) { KmhNotifications.Rejected("Unknown item"); return false; }
            Caravan caravan = CaravanReader.GetSelectedCaravan();
            Map     map     = caravan == null ? ColonyGoods.DepositMap() : null;
            string  src     = caravan != null ? "caravan" : "colony";
            int have = caravan != null ? ColonyGoods.CountKey(caravan, itemDefName) : ColonyGoods.CountOnMapKey(map, itemDefName);
            if (have < qty) { KmhNotifications.Rejected($"Your {src} only has {have} {ItemKeys.LabelForKey(itemDefName)}"); return false; }
            bool complex = !Items.KmhThingCapture.IsSimple(def);
            return SendPreflight(new DepositIntent { Kind = PendingDeposit.KindItem, ItemDefName = itemDefName, Qty = qty, Complex = complex },
                new System.Collections.Generic.Dictionary<string, object>
                    { { "kind", PendingDeposit.KindItem }, { "item_def_name", itemDefName }, { "qty", qty }, { "is_payload", complex } },
                $"Requesting approval to deposit ×{qty} {ItemKeys.LabelForKey(itemDefName)}…");
        }

        // Approvals belong to the connection that was asked. A reply that outlives its session must not remove goods.
        internal static void ClearInFlight() => _pending.Clear();

        private static bool SendPreflight(DepositIntent intent, System.Collections.Generic.Dictionary<string, object> fields, string flash)
        {
            string reqId = NewTxn();
            fields["req_id"] = reqId;
            _pending[reqId] = intent;
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositPreflight, EconomyCtx.With(fields));
            if (sent) KmhNotifications.Neutral(flash);
            else { _pending.Remove(reqId); KmhNotifications.NotConnected(); }
            return sent;
        }

        // Approval is the point local goods are removed; a rejection has removed nothing and needs no recovery.
        private static void OnDepositApproval(KmhEnvelope env)
        {
            string reqId = env?.GetString("req_id") ?? "";
            if (string.IsNullOrEmpty(reqId) || !_pending.TryGetValue(reqId, out DepositIntent intent)) return;
            _pending.Remove(reqId);
            if (!(env?.GetBool("ok", false) ?? false))
            {
                // Declined as complex: retry as a payload. The !Complex guard is what stops this looping.
                if (intent.Kind == PendingDeposit.KindItem && !intent.Complex && (env?.GetBool("needs_payload", false) ?? false))
                {
                    intent.Complex = true;
                    SendPreflight(intent,
                        new System.Collections.Generic.Dictionary<string, object>
                            { { "kind", PendingDeposit.KindItem }, { "item_def_name", intent.ItemDefName }, { "qty", intent.Qty }, { "is_payload", true } },
                        $"Re-sending ×{intent.Qty} {ItemKeys.LabelForKey(intent.ItemDefName)} with full item state…");
                    return;
                }
                KmhNotifications.Rejected(env?.GetString("reason") ?? "Deposit not allowed right now.");
                return;
            }
            string token = env?.GetString("token") ?? "";
            if (intent.Kind == PendingDeposit.KindSilver) CompleteSilver(intent.Amount, token);
            else CompleteItem(intent, token);
        }

        private static void CompleteSilver(int amount, string token)
        {
            Caravan caravan = CaravanReader.GetSelectedCaravan();
            Map     map     = caravan == null ? ColonyGoods.DepositMap() : null;
            string  src     = caravan != null ? "caravan" : "colony";
            bool removed = caravan != null ? ColonyGoods.TryRemoveSilver(caravan, amount) : ColonyGoods.TryRemoveSilverOnMap(map, amount);
            if (!removed) { KmhNotifications.Rejected($"Could not take the silver from your {src}"); return; }
            string txn = NewTxn();
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositSilver,
                EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "amount", amount }, { "txn_id", txn }, { "deposit_token", token } }));
            if (sent)
            {
                RecordAndRefresh(txn);
                KmhNotifications.FlashCoalesced("deposit", $"Deposited {amount} silver - save your game to finalize",
                    n => $"{n} deposits pending - save your game to finalize", RimWorld.MessageTypeDefOf.PositiveEvent);
            }
            else if (ColonyGoods.DeliverSilver(amount)) KmhNotifications.Rejected("Not connected - silver returned");
            else ReturnFailed($"{amount} silver");
        }

        private static void CompleteItem(DepositIntent intent, string token)
        {
            string itemDefName = intent.ItemDefName; int qty = intent.Qty;
            Caravan caravan = CaravanReader.GetSelectedCaravan();
            Map     map     = caravan == null ? ColonyGoods.DepositMap() : null;
            string  src     = caravan != null ? "caravan" : "colony";
            if (intent.Complex)
            {
                if (!ColonyGoods.RemoveKeyCapturing(caravan, map, itemDefName, qty, out var payloads, out string why) || payloads.Count == 0)
                { KmhNotifications.Rejected(why ?? $"Could not take the items from your {src}"); return; }
                string txnP = NewTxn();
                bool sentP = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositItem,
                    EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object>
                        { { "item_def_name", itemDefName }, { "qty", qty }, { "payloads", payloads }, { "txn_id", txnP }, { "deposit_token", token } }));
                if (sentP)
                {
                    RecordAndRefresh(txnP);
                    KmhNotifications.FlashCoalesced("deposit", $"Deposited ×{qty} {ItemKeys.LabelForKey(itemDefName)} (full state kept) - save your game to finalize",
                        n => $"{n} deposits pending - save your game to finalize", RimWorld.MessageTypeDefOf.PositiveEvent);
                }
                else if (ColonyGoods.DeliverPayloads(payloads)) KmhNotifications.Rejected("Not connected - items returned");
                else ReturnFailed($"×{qty} {ItemKeys.LabelForKey(itemDefName)}");
                return;
            }
            bool removed = caravan != null ? ColonyGoods.TryRemoveKey(caravan, itemDefName, qty) : ColonyGoods.TryRemoveOnMapKey(map, itemDefName, qty);
            if (!removed) { KmhNotifications.Rejected($"Could not take the items from your {src}"); return; }
            string txn = NewTxn();
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositItem,
                EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "item_def_name", itemDefName }, { "qty", qty }, { "txn_id", txn }, { "deposit_token", token } }));
            if (sent)
            {
                RecordAndRefresh(txn);
                KmhNotifications.FlashCoalesced("deposit", $"Deposited ×{qty} {ItemKeys.LabelForKey(itemDefName)} - save your game to finalize",
                    n => $"{n} deposits pending - save your game to finalize", RimWorld.MessageTypeDefOf.PositiveEvent);
            }
            else if (ColonyGoods.DeliverKey(itemDefName, qty)) KmhNotifications.Rejected("Not connected - items returned");
            else ReturnFailed($"×{qty} {ItemKeys.LabelForKey(itemDefName)}");
        }

        // Send failed AND the goods wouldn't go back: rare, but the value is gone, so alert loudly.
        private static void ReturnFailed(string desc)
        {
            KmhNotifications.Alert("Deposit not returned",
                $"KMH took {desc} for a deposit, the server didn't receive it, and it couldn't be returned to your " +
                "colony. Take a screenshot and tell the server owner.");
        }

        private static void RecordAndRefresh(string txn)
        {
            GameComponent_KMHDepositLedger.Instance?.RecordDeposit(txn);
            // Vanilla wealth still counts the departed goods for ~83s, so the storyteller would read them twice.
            Features.Wealth.KmhWealthLedger.NudgeMapRecount();
            RequestSnapshot();
        }

        // No caravan needed: the grant goes to the selected caravan, else it drops on the home map.
        public static bool TryWithdrawSilver(int amount)
        {
            if (amount <= 0) { KmhNotifications.Rejected("Amount must be greater than 0"); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryWithdrawSilver,
                EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "amount", amount } }),
                KmhOpId.For($"treasury.withdraw_silver|{amount}"));
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryWithdrawItem(string itemDefName, int qty)
        {
            if (string.IsNullOrEmpty(itemDefName)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryWithdrawItem,
                EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "item_def_name", itemDefName }, { "qty", qty } }),
                KmhOpId.For($"treasury.withdraw_item|{itemDefName}|{qty}"));
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        // Withdraw a state-preserving payload stack by its fingerprint - the server returns the exact captured items.
        public static bool TryWithdrawPayload(string fingerprint, int qty)
        {
            if (string.IsNullOrEmpty(fingerprint)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryWithdrawItem,
                EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "fingerprint", fingerprint }, { "qty", qty } }),
                KmhOpId.For($"treasury.withdraw_item|{fingerprint}|{qty}"));
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        // Deferred to the main thread: handlers run on the network thread and spawning mutates game state.
        private static void OnGrant(KmhEnvelope env)
        {
            if (env == null) return;
            string kind = env.GetString("kind");

            // Dedupe and record both wait for the deferred delivery: a replay arrives mid-load with no Game to ask.
            string deliveryId = env.GetString("delivery_id") ?? "";

            // Full-state payload grant (complex items).
            if (kind == "item_payloads")
            {
                var req = env.DataAs<Dto.TreasuryGrantPayloads>();
                if (req?.Payloads == null || req.Payloads.Count == 0) return;
                int total = 0; foreach (var p in req.Payloads) total += System.Math.Max(1, p?.StackCount ?? 0);
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (Delivery.GameComponent_KMHDeliveryReceipts.HeldAlready(deliveryId)) return;
                    string desc = $"×{total} item(s)";
                    // Asked BEFORE anything is built: arguments evaluate left to right, and a map appearing between the two calls read as "do not retry".
                    bool canDeliver = ColonyGoods.CanDeliverNow();
                    switch (Delivery.KmhGrantDecision.Decide(canDeliver && ColonyGoods.DeliverPayloads(req.Payloads), canDeliver))
                    {
                        case Delivery.KmhGrantOutcome.Delivered:
                            Delivery.GameComponent_KMHDeliveryReceipts.RecordDelivered(deliveryId);
                            KmhNotifications.Positive($"Received {desc}");
                            Extensibility.KmhClientEventBus.Instance.RaiseGrantReceived(
                                new KMH.Sdk.Client.Events.KmhGrantReceivedEvent { Kind = "item_payloads", Quantity = total });
                            break;
                        case Delivery.KmhGrantOutcome.Held:      Delivery.KmhPendingDelivery.HoldPayloads(req.Payloads, desc, deliveryId); break;
                        default:                                 GrantUndelivered(desc); break;
                    }
                });
                return;
            }

            int    amount  = env.GetInt("amount", 0);
            string defName = env.GetString("def_name");
            if (amount <= 0) return;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Delivery.GameComponent_KMHDeliveryReceipts.HeldAlready(deliveryId)) return;
                if (kind == "item")
                {
                    // DeliverKey restores material+quality from the composed key, and returns false for a removed mod's def.
                    string desc = $"×{amount} {ItemKeys.LabelForKey(defName)}";
                    bool canDeliver = ColonyGoods.CanDeliverNow();
                    switch (Delivery.KmhGrantDecision.Decide(canDeliver && ColonyGoods.DeliverKey(defName, amount), canDeliver))
                    {
                        case Delivery.KmhGrantOutcome.Delivered:
                            Delivery.GameComponent_KMHDeliveryReceipts.RecordDelivered(deliveryId);
                            KmhNotifications.Positive($"Received {desc}");
                            Extensibility.KmhClientEventBus.Instance.RaiseGrantReceived(
                                new KMH.Sdk.Client.Events.KmhGrantReceivedEvent { Kind = "item", ItemDefName = defName, Quantity = amount });
                            break;
                        case Delivery.KmhGrantOutcome.Held:      Delivery.KmhPendingDelivery.HoldItemKey(defName, amount, desc, deliveryId); break;
                        default:                                 GrantUndelivered(desc); break;
                    }
                }
                else
                {
                    string desc = $"{amount} silver";
                    bool canDeliver = ColonyGoods.CanDeliverNow();
                    switch (Delivery.KmhGrantDecision.Decide(canDeliver && ColonyGoods.DeliverSilver(amount), canDeliver))
                    {
                        case Delivery.KmhGrantOutcome.Delivered:
                            Delivery.GameComponent_KMHDeliveryReceipts.RecordDelivered(deliveryId);
                            KmhNotifications.Positive($"Received {desc}");
                            Extensibility.KmhClientEventBus.Instance.RaiseGrantReceived(
                                new KMH.Sdk.Client.Events.KmhGrantReceivedEvent { Kind = "silver", Silver = amount });
                            break;
                        case Delivery.KmhGrantOutcome.Held:      Delivery.KmhPendingDelivery.HoldSilver(amount, desc, deliveryId); break;
                        default:                                 GrantUndelivered(desc); break;
                    }
                }
            });
        }

        // Already debited and unplaceable even with a drop site (no-drop-site is held for retry instead), so never silent.
        private static void GrantUndelivered(string desc)
        {
            KmhNotifications.Alert("Withdrawal not delivered",
                $"KMH withdrew {desc} from your vault but couldn't place it in your colony (a missing mod or no valid " +
                "drop cell). Your vault was already charged. Take a screenshot and ask the server owner to restore it.");
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            TreasurySnapshot snapshot = env?.DataAs<TreasurySnapshot>();
            if (snapshot == null)
            {
                KmhLog.Warn("Treasury snapshot envelope had no parseable payload, ignoring");
                return;
            }
            TreasuryCache.Apply(snapshot);
            // If this snapshot still shows deposits we've durably saved, re-confirm them now instead of waiting a tick.
            GameComponent_KMHDepositLedger.Instance?.OnSnapshotApplied();
        }
    }
}
