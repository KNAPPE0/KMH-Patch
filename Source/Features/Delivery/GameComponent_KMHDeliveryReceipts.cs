using System.Collections.Generic;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Features.Delivery
{
    // A re-sent grant is only safe to ignore if the proof of holding it survives a reload, so the ack waits for the scribe rather than the socket.
    public class GameComponent_KMHDeliveryReceipts : GameComponent
    {
        private const int MaxReceipts = 512;

        private List<string> _durable = new List<string>();
        private readonly HashSet<string> _thisSession = new HashSet<string>();
        private readonly List<string> _unacked = new List<string>();
        private readonly HashSet<string> _acked = new HashSet<string>();

        // The owed set only changes on a save or a new server, so a pass that finds nothing left stops scanning until then.
        private bool _rescanNeeded = true;

        public GameComponent_KMHDeliveryReceipts(Game game) { }

        public static GameComponent_KMHDeliveryReceipts Instance => Current.Game?.GetComponent<GameComponent_KMHDeliveryReceipts>();

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving && _thisSession.Count > 0)
            {
                foreach (string id in _thisSession) if (!_durable.Contains(id)) _durable.Add(id);
                while (_durable.Count > MaxReceipts) _durable.RemoveAt(0);
            }
            Scribe_Collections.Look(ref _durable, "kmhDeliveryReceipts", LookMode.Value);
            if (_durable == null) _durable = new List<string>();
            _rescanNeeded = true;   // a save just promoted this session's ids, and a load brings a fresh list
        }

        // Session-only is enough to stop a reconnect replay landing twice before any save has happened.
        public bool AlreadyHeld(string deliveryId)
            => !string.IsNullOrEmpty(deliveryId) && (_thisSession.Contains(deliveryId) || _durable.Contains(deliveryId));

        public void Record(string deliveryId)
        {
            if (!string.IsNullOrEmpty(deliveryId)) _thisSession.Add(deliveryId);
        }

        // Instance is null until the Game exists, which is exactly when a replayed grant arrives; asked from the deferred delivery, not on arrival.
        internal static bool HeldAlready(string deliveryId) => Instance?.AlreadyHeld(deliveryId) ?? false;

        // Only a delivery that actually landed: the colony now holds the goods, and the save captures that.
        internal static void RecordDelivered(string deliveryId) => Instance?.Record(deliveryId);

        // Only ids the save actually holds are acked: a session-only one would let a crash discharge a delivery the reloaded game has no record of.
        public override void GameComponentUpdate()
        {
            if (!_rescanNeeded || _durable.Count == 0 || !KmhDispatcher.IsKmhServer) return;
            for (int i = _durable.Count - 1; i >= 0 && _unacked.Count < 32; i--)
                if (!_acked.Contains(_durable[i])) _unacked.Add(_durable[i]);
            if (_unacked.Count == 0) { _rescanNeeded = false; return; }
            // A refused send leaves the id owed, and a pass capped at 32 leaves the rest, so keep scanning.
            foreach (string id in _unacked)
                if (KmhDispatcher.Send(KmhProtocol.Kind.DeliveryAck, new { delivery_id = id })) _acked.Add(id);
            _unacked.Clear();
        }

        // Only the per-session ack bookkeeping resets, so the next server's owed list gets acknowledged again and nothing is dropped.
        internal void ForgetAcksForNewSession() { _acked.Clear(); _rescanNeeded = true; }
    }
}
