using System;
using System.Collections.Generic;
using KMHPatch.Items;
using KMHPatch.Notifications;
using KMHPatch.UI;

namespace KMHPatch.Features.Delivery
{
    // In-memory only, with the loud letter as the durable floor; the retry drops an entry whatever happens, so nothing re-drops.
    internal static class KmhPendingDelivery
    {
        private enum Kind { Silver, ItemKey, Payloads }

        private sealed class Entry
        {
            public Kind Kind;
            public int Amount;
            public string Key;
            public List<KmhThingPayload> Payloads;
            public string Label;
            // Carried so the server's replay can be acknowledged when the HELD copy lands, not when it was received.
            public string DeliveryId;
            public DateTime QueuedUtc;
            public bool Alerted;
        }

        private const double StuckSeconds = 60.0;   // how long held-and-undeliverable before we raise the "ask the owner" letter

        private static readonly object _lock = new object();
        private static readonly List<Entry> _queue = new List<Entry>();

        // Lock-free because the driver polls IsEmpty every frame; staleness only delays a pump by one frame.
        private static volatile int _count;
        public static bool IsEmpty => _count == 0;

        public static void HoldSilver(int amount, string label, string deliveryId = "")
            => Add(new Entry { Kind = Kind.Silver, Amount = amount, Label = label, DeliveryId = deliveryId });
        public static void HoldItemKey(string key, int amount, string label, string deliveryId = "")
            => Add(new Entry { Kind = Kind.ItemKey, Key = key, Amount = amount, Label = label, DeliveryId = deliveryId });
        public static void HoldPayloads(List<KmhThingPayload> payloads, string label, string deliveryId = "")
            => Add(new Entry { Kind = Kind.Payloads, Payloads = payloads, Label = label, DeliveryId = deliveryId });

        private static void Add(Entry e)
        {
            e.QueuedUtc = DateTime.UtcNow;
            lock (_lock) { _queue.Add(e); _count = _queue.Count; }
            KmhNotifications.Neutral($"Holding {e.Label} - it'll arrive once you have a colony or a selected caravan.");
        }

        // Anything held too long escalates once to a loud letter, so a stuck grant is never silently forgotten.
        public static void Pump()
        {
            List<Entry> snapshot;
            lock (_lock) { if (_queue.Count == 0) return; snapshot = new List<Entry>(_queue); }

            bool canDeliver = ColonyGoods.CanDeliverNow();
            foreach (Entry e in snapshot)
            {
                if (canDeliver)
                {
                    bool ok = TryDeliver(e);
                    lock (_lock) { _queue.Remove(e); _count = _queue.Count; }   // one attempt then stop, so a partial retry can't duplicate
                    if (ok)
                    {
                        // Acked here and only here for a held grant: this is the moment the colony actually holds it.
                        GameComponent_KMHDeliveryReceipts.RecordDelivered(e.DeliveryId);
                        KmhNotifications.Positive($"Received {e.Label} (held delivery)");
                        Extensibility.KmhClientEventBus.Instance.RaiseGrantReceived(GrantEventFor(e));   // same "received" signal as an immediate grant
                    }
                    else Escalate(e, "even though a drop site was available (a missing mod or no valid drop cell)");
                    continue;
                }
                if (!e.Alerted && (DateTime.UtcNow - e.QueuedUtc).TotalSeconds >= StuckSeconds)
                {
                    e.Alerted = true;
                    Escalate(e, "and there's still nowhere to place it - it'll deliver automatically once you have a colony or a selected caravan");
                }
            }
        }

        private static KMH.Sdk.Client.Events.KmhGrantReceivedEvent GrantEventFor(Entry e)
        {
            switch (e.Kind)
            {
                case Kind.Silver:   return new KMH.Sdk.Client.Events.KmhGrantReceivedEvent { Kind = "silver", Silver = e.Amount };
                case Kind.ItemKey:  return new KMH.Sdk.Client.Events.KmhGrantReceivedEvent { Kind = "item", ItemDefName = e.Key, Quantity = e.Amount };
                default:            int total = 0; if (e.Payloads != null) foreach (var p in e.Payloads) total += Math.Max(1, p?.StackCount ?? 0);
                                    return new KMH.Sdk.Client.Events.KmhGrantReceivedEvent { Kind = "item_payloads", Quantity = total };
            }
        }

        private static bool TryDeliver(Entry e)
        {
            switch (e.Kind)
            {
                case Kind.Silver:   return ColonyGoods.DeliverSilver(e.Amount);
                case Kind.ItemKey:  return ColonyGoods.DeliverKey(e.Key, e.Amount);
                case Kind.Payloads: return ColonyGoods.DeliverPayloads(e.Payloads);
                default:            return false;
            }
        }

        private static void Escalate(Entry e, string why)
        {
            KmhNotifications.Alert("Withdrawal not delivered",
                $"KMH is holding {e.Label} that your vault was already charged for, {why}. If it never arrives, take a " +
                "screenshot and ask the server owner to restore it.");
        }
    }
}
