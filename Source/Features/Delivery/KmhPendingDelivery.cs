using System;
using System.Collections.Generic;
using KMHPatch.Items;
using KMHPatch.Notifications;
using KMHPatch.UI;

namespace KMHPatch.Features.Delivery
{
    // In-memory, with the loud letter as the durable floor; a retry drops its entry whatever happens, so nothing re-drops.
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
            public int Generation;
        }

        private const double StuckSeconds = 60.0;

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

        // One held entry per delivery: the server replays every owed delivery on each reconnect, so re-queueing duplicates.
        private static void Add(Entry e)
        {
            e.QueuedUtc = DateTime.UtcNow;
            e.Generation = GenerationSource();
            lock (_lock)
            {
                DropStaleLocked();
                if (!string.IsNullOrEmpty(e.DeliveryId))
                    foreach (Entry held in _queue)
                        if (string.Equals(held.DeliveryId, e.DeliveryId, StringComparison.Ordinal)) return;
                _queue.Add(e);
                _count = _queue.Count;
            }
            // The notice is a courtesy; the hold above is what keeps the goods, so a notice that cannot show changes nothing.
            try { KmhNotifications.Neutral($"Holding {e.Label} - it'll arrive once you have a colony or a selected caravan."); }
            catch (Exception) { }
        }

        // A held delivery belongs to the session that replayed it; one from an earlier session must not land in the next game.
        internal static Func<int> GenerationSource = () => SubProtocol.KmhDispatcher.SessionGeneration;

        private static void DropStaleLocked()
        {
            int now = GenerationSource();
            _queue.RemoveAll(e => !string.IsNullOrEmpty(e.DeliveryId) && e.Generation != now);
            _count = _queue.Count;
        }

        internal static int CountForTest { get { lock (_lock) return _queue.Count; } }
        internal static void ClearForTest() { lock (_lock) { _queue.Clear(); _count = 0; } }

        // Anything held too long escalates once to a loud letter, so a stuck grant is never silently forgotten.
        public static void Pump()
        {
            List<Entry> snapshot;
            lock (_lock) { DropStaleLocked(); if (_queue.Count == 0) return; snapshot = new List<Entry>(_queue); }

            bool canDeliver = ColonyGoods.CanDeliverNow();
            foreach (Entry e in snapshot)
            {
                if (canDeliver)
                {
                    // Landed already by another path (a replay that arrived after a drop site appeared): the colony holds it.
                    if (GameComponent_KMHDeliveryReceipts.HeldAlready(e.DeliveryId))
                    {
                        lock (_lock) { _queue.Remove(e); _count = _queue.Count; }
                        continue;
                    }
                    ColonyGoods.PayloadDelivery result;
                    string blocked, failed;
                    // One attempt then stop, so a partial retry can't duplicate - a throw part-way included.
                    try { result = TryDeliver(e, out blocked, out failed); }
                    finally { lock (_lock) { _queue.Remove(e); _count = _queue.Count; } }
                    switch (result)
                    {
                        case ColonyGoods.PayloadDelivery.Delivered:
                            // Acked here and only here for a held grant: this is the moment the colony actually holds it.
                            GameComponent_KMHDeliveryReceipts.RecordDelivered(e.DeliveryId);
                            KmhNotifications.Positive($"Received {e.Label} (held delivery)");
                            Extensibility.KmhClientEventBus.Instance.RaiseGrantReceived(GrantEventFor(e));   // same "received" signal as an immediate grant
                            break;
                        case ColonyGoods.PayloadDelivery.Blocked: KmhGrantNotice.Blocked(e.Label, blocked, e.DeliveryId); break;
                        case ColonyGoods.PayloadDelivery.Partial:
                            GameComponent_KMHDeliveryReceipts.RecordDelivered(e.DeliveryId);
                            KmhGrantNotice.PartlyDelivered(e.Label, failed);
                            break;
                        default: Escalate(e, "even though a drop site was available (no valid drop cell)"); break;
                    }
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

        private static ColonyGoods.PayloadDelivery TryDeliver(Entry e, out string blocked, out string failed)
        {
            blocked = null; failed = null;
            ColonyGoods.PayloadDelivery Of(bool ok) => ok ? ColonyGoods.PayloadDelivery.Delivered : ColonyGoods.PayloadDelivery.Failed;
            switch (e.Kind)
            {
                case Kind.Silver:   return Of(ColonyGoods.DeliverSilver(e.Amount));
                case Kind.ItemKey:
                    blocked = ColonyGoods.WhyCannotDeliverKey(e.Key);
                    return blocked != null ? ColonyGoods.PayloadDelivery.Blocked : Of(ColonyGoods.DeliverKey(e.Key, e.Amount));
                case Kind.Payloads: return ColonyGoods.DeliverPayloads(e.Payloads, out blocked, out failed);
                default:            return ColonyGoods.PayloadDelivery.Failed;
            }
        }

        private static void Escalate(Entry e, string why)
        {
            KmhNotifications.Alert("Withdrawal not delivered",
                $"Colony Nexus is holding {e.Label} that your vault was already charged for, {why}. If it never arrives, take a " +
                "screenshot and ask the server owner to restore it.");
        }
    }
}
