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
    // Treasury mutations now move REAL goods. Deposits verify the selected caravan actually holds the silver/items
    // and remove them before telling the server (rolled back if the send fails). Withdrawals require a caravan and
    // only materialize once the server confirms the debit via kmh.treasury.grant
    internal static class TreasuryHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.TreasurySnapshot, OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.TreasuryGrant,    OnGrant);
        }

        public static bool RequestSnapshot()
            => KmhDispatcher.Send(KmhProtocol.Kind.TreasuryRequest, null);

        // -- deposits: verify + remove from the caravan, then credit the server --

        public static bool TryDepositSilver(int amount)
        {
            if (amount <= 0) { KmhNotifications.Rejected("Amount must be greater than 0"); return false; }

            Caravan caravan = ColonyGoods.RequireCaravan(out string err);
            if (caravan == null) { KmhNotifications.Rejected(err); return false; }

            int have = ColonyGoods.CountSilver(caravan);
            if (have < amount) { KmhNotifications.Rejected($"Your caravan only has {have} silver"); return false; }

            if (!ColonyGoods.TryRemoveSilver(caravan, amount))
            { KmhNotifications.Rejected("Could not take the silver from the caravan"); return false; }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositSilver, new { amount });
            if (sent) KmhNotifications.Positive($"Deposited {amount} silver");
            else { ColonyGoods.GiveSilver(caravan, amount); KmhNotifications.Rejected("Not connected - silver returned to your caravan"); }
            return sent;
        }

        public static bool TryDepositItem(string itemDefName, int qty)
        {
            if (string.IsNullOrEmpty(itemDefName)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }

            // itemDefName may be a composed key (def|stuff|quality) straight from the caravan picker
            ItemKeys.Split(itemDefName, out string pureDef, out _, out _);
            if (ColonyGoods.Def(pureDef) == null) { KmhNotifications.Rejected("Unknown item"); return false; }

            Caravan caravan = ColonyGoods.RequireCaravan(out string err);
            if (caravan == null) { KmhNotifications.Rejected(err); return false; }

            int have = ColonyGoods.CountKey(caravan, itemDefName);
            if (have < qty) { KmhNotifications.Rejected($"Your caravan only has {have} {ItemKeys.LabelForKey(itemDefName)}"); return false; }

            if (!ColonyGoods.TryRemoveKey(caravan, itemDefName, qty))
            { KmhNotifications.Rejected("Could not take the items from the caravan"); return false; }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositItem, new { item_def_name = itemDefName, qty });
            if (sent) KmhNotifications.Positive($"Deposited ×{qty} {ItemKeys.LabelForKey(itemDefName)}");
            else { ColonyGoods.DeliverKey(itemDefName, qty); KmhNotifications.Rejected("Not connected - items returned to your caravan"); }
            return sent;
        }

        // -- withdrawals: require a caravan, then wait for the server's grant --

        // Withdrawals don't need a caravan - the grant is delivered to the selected caravan if there is one,
        // otherwise dropped on the home map
        public static bool TryWithdrawSilver(int amount)
        {
            if (amount <= 0) { KmhNotifications.Rejected("Amount must be greater than 0"); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryWithdrawSilver, new { amount });
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TryWithdrawItem(string itemDefName, int qty)
        {
            if (string.IsNullOrEmpty(itemDefName)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryWithdrawItem, new { item_def_name = itemDefName, qty });
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Server confirmed a withdrawal - materialize it into the colony (caravan or a drop pod). Deferred to the
        // main thread because spawning mutates game state
        private static void OnGrant(KmhEnvelope env)
        {
            if (env == null) return;
            string kind    = env.GetString("kind");
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
        }
    }
}
