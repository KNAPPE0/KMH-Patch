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
        }

        public static bool RequestSnapshot()
            => KmhDispatcher.Send(KmhProtocol.Kind.TreasuryRequest, null);

        // -- deposits: verify + remove from the caravan, then credit the server --

        public static bool TryDepositSilver(int amount)
        {
            if (amount <= 0) { KmhNotifications.Rejected("Amount must be greater than 0"); return false; }

            // Source = the selected caravan if there is one, otherwise the colony's stockpiles. No caravan required.
            Caravan caravan = CaravanReader.GetSelectedCaravan();
            Map     map     = caravan == null ? ColonyGoods.DepositMap() : null;
            string  src     = caravan != null ? "caravan" : "colony";

            int have = caravan != null ? ColonyGoods.CountSilver(caravan) : ColonyGoods.CountSilverOnMap(map);
            if (have < amount) { KmhNotifications.Rejected($"Your {src} only has {have} silver"); return false; }

            bool removed = caravan != null
                ? ColonyGoods.TryRemoveSilver(caravan, amount)
                : ColonyGoods.TryRemoveSilverOnMap(map, amount);
            if (!removed) { KmhNotifications.Rejected($"Could not take the silver from your {src}"); return false; }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositSilver, new { amount });
            if (sent) KmhNotifications.Positive($"Deposited {amount} silver");
            else { ColonyGoods.DeliverSilver(amount); KmhNotifications.Rejected("Not connected - silver returned"); }
            return sent;
        }

        public static bool TryDepositItem(string itemDefName, int qty)
        {
            if (string.IsNullOrEmpty(itemDefName)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }

            // itemDefName may be a composed key (def|stuff|quality) straight from the picker
            ItemKeys.Split(itemDefName, out string pureDef, out _, out _);
            if (ColonyGoods.Def(pureDef) == null) { KmhNotifications.Rejected("Unknown item"); return false; }

            // Source = the selected caravan if there is one, otherwise the colony's stockpiles. No caravan required.
            Caravan caravan = CaravanReader.GetSelectedCaravan();
            Map     map     = caravan == null ? ColonyGoods.DepositMap() : null;
            string  src     = caravan != null ? "caravan" : "colony";

            int have = caravan != null ? ColonyGoods.CountKey(caravan, itemDefName) : ColonyGoods.CountOnMapKey(map, itemDefName);
            if (have < qty) { KmhNotifications.Rejected($"Your {src} only has {have} {ItemKeys.LabelForKey(itemDefName)}"); return false; }

            bool removed = caravan != null
                ? ColonyGoods.TryRemoveKey(caravan, itemDefName, qty)
                : ColonyGoods.TryRemoveOnMapKey(map, itemDefName, qty);
            if (!removed) { KmhNotifications.Rejected($"Could not take the items from your {src}"); return false; }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.TreasuryDepositItem, new { item_def_name = itemDefName, qty });
            if (sent) KmhNotifications.Positive($"Deposited ×{qty} {ItemKeys.LabelForKey(itemDefName)}");
            else { ColonyGoods.DeliverKey(itemDefName, qty); KmhNotifications.Rejected("Not connected - items returned"); }
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
