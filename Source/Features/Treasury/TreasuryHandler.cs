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
    // Treasury mutations move REAL goods. Deposits verify the source (selected caravan, else colony stockpiles)
    // actually holds the silver/items and remove them before telling the server (rolled back if the send fails).
    // Withdrawals only materialize once the server confirms the debit via kmh.treasury.grant.
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

        // -- deposits: PREFLIGHT first (server approves before we touch local goods), then remove + send with the token.
        // Removing only AFTER approval means a rejected deposit never loses items/silver and needs no admin recovery. --

        // A KMH deposit txn id: ties the local goods-removal to the server's pending deposit so the credit is only
        // finalized once this removal is durably saved. Fixes the disconnect/rollback dupe.
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
            return SendPreflight(new DepositIntent { Kind = "silver", Amount = amount },
                new System.Collections.Generic.Dictionary<string, object> { { "kind", "silver" }, { "amount", amount } },
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
            return SendPreflight(new DepositIntent { Kind = "item", ItemDefName = itemDefName, Qty = qty, Complex = complex },
                new System.Collections.Generic.Dictionary<string, object>
                    { { "kind", "item" }, { "item_def_name", itemDefName }, { "qty", qty }, { "is_payload", complex } },
                $"Requesting approval to deposit ×{qty} {ItemKeys.LabelForKey(itemDefName)}…");
        }

        private static bool SendPreflight(DepositIntent intent, System.Collections.Generic.Dictionary<string, object> fields, string flash)
        {
            string reqId = NewTxn();
            fields["req_id"] = reqId;
            _pending[reqId] = intent;
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositPreflight, EconomyCtx.With(fields));
            if (sent) KmhNotifications.Neutral(flash);
            else { _pending.Remove(reqId); KmhNotifications.Rejected("Not connected to a KMH server"); }
            return sent;
        }

        // Server replied to a preflight. On approval we NOW remove local goods and send the real deposit with the token;
        // on rejection nothing was removed, so there is no loss and no recovery needed.
        private static void OnDepositApproval(KmhEnvelope env)
        {
            string reqId = env?.GetString("req_id") ?? "";
            if (string.IsNullOrEmpty(reqId) || !_pending.TryGetValue(reqId, out DepositIntent intent)) return;
            _pending.Remove(reqId);
            if (!(env?.GetBool("ok", false) ?? false))
            { KmhNotifications.Rejected(env?.GetString("reason") ?? "Deposit not allowed right now."); return; }
            string token = env?.GetString("token") ?? "";
            if (intent.Kind == "silver") CompleteSilver(intent.Amount, token);
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
            else { ColonyGoods.DeliverSilver(amount); KmhNotifications.Rejected("Not connected - silver returned"); }
        }

        private static void CompleteItem(DepositIntent intent, string token)
        {
            string itemDefName = intent.ItemDefName; int qty = intent.Qty;
            Caravan caravan = CaravanReader.GetSelectedCaravan();
            Map     map     = caravan == null ? ColonyGoods.DepositMap() : null;
            string  src     = caravan != null ? "caravan" : "colony";
            if (intent.Complex)
            {
                if (!ColonyGoods.RemoveKeyCapturing(caravan, map, itemDefName, qty, out var payloads) || payloads.Count == 0)
                { KmhNotifications.Rejected($"Could not take the items from your {src}"); return; }
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
                else { ColonyGoods.DeliverPayloads(payloads); KmhNotifications.Rejected("Not connected - items returned"); }
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
            else { ColonyGoods.DeliverKey(itemDefName, qty); KmhNotifications.Rejected("Not connected - items returned"); }
        }

        // Record the durable-deposit txn locally (confirmed to the server once the game is saved) and pull a fresh
        // treasury snapshot so the pending-deposit line and balances refresh right away.
        private static void RecordAndRefresh(string txn)
        {
            GameComponent_KMHDepositLedger.Instance?.RecordDeposit(txn);
            RequestSnapshot();
        }

        // -- withdrawals: require a caravan, then wait for the server's grant --

        // Withdrawals don't need a caravan - the grant is delivered to the selected caravan if there is one,
        // otherwise dropped on the home map
        public static bool TryWithdrawSilver(int amount)
        {
            if (amount <= 0) { KmhNotifications.Rejected("Amount must be greater than 0"); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryWithdrawSilver,
                EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "amount", amount } }));
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TryWithdrawItem(string itemDefName, int qty)
        {
            if (string.IsNullOrEmpty(itemDefName)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryWithdrawItem,
                EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "item_def_name", itemDefName }, { "qty", qty } }));
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Withdraw a state-preserving payload stack by its fingerprint - the server returns the exact captured items.
        public static bool TryWithdrawPayload(string fingerprint, int qty)
        {
            if (string.IsNullOrEmpty(fingerprint)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryWithdrawItem,
                EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "fingerprint", fingerprint }, { "qty", qty } }));
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Server confirmed a withdrawal - materialize it into the colony (caravan or a drop pod). Deferred to the
        // main thread because spawning mutates game state
        private static void OnGrant(KmhEnvelope env)
        {
            if (env == null) return;
            string kind = env.GetString("kind");

            // Full-state payload grant (complex items).
            if (kind == "item_payloads")
            {
                var req = env.DataAs<Dto.TreasuryGrantPayloads>();
                if (req?.Payloads == null || req.Payloads.Count == 0) return;
                int total = 0; foreach (var p in req.Payloads) total += System.Math.Max(1, p?.StackCount ?? 0);
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    ColonyGoods.DeliverPayloads(req.Payloads);
                    KmhNotifications.Positive($"Received ×{total} item(s)");
                });
                return;
            }

            int    amount  = env.GetInt("amount", 0);
            string defName = env.GetString("def_name");
            if (amount <= 0) return;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (kind == "item")
                {
                    // defName may be a composed key - DeliverKey restores material + quality on spawn
                    ItemKeys.Split(defName, out string pureDef, out _, out _);
                    if (ColonyGoods.Def(pureDef) == null) { KmhLog.Warn($"Treasury grant for unknown item '{defName}'"); return; }
                    ColonyGoods.DeliverKey(defName, amount);
                    KmhNotifications.Positive($"Received ×{amount} {ItemKeys.LabelForKey(defName)}");
                }
                else
                {
                    ColonyGoods.DeliverSilver(amount);
                    KmhNotifications.Positive($"Received {amount} silver");
                }
            });
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
