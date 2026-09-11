using System;
using Verse;

namespace KMHPatch.Features.Delivery
{
    // Drives the pending queue so already-granted goods land the moment a colony or selected caravan exists.
    public class GameComponent_KMHDelivery : GameComponent
    {
        private const double PumpSeconds = 2.0;
        private DateTime _lastPumpUtc = DateTime.MinValue;

        public GameComponent_KMHDelivery(Game game) { }

        public override void GameComponentUpdate()
        {
            if (KmhPendingDelivery.IsEmpty) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastPumpUtc).TotalSeconds < PumpSeconds) return;
            _lastPumpUtc = now;
            KmhPendingDelivery.Pump();
        }
    }
}
