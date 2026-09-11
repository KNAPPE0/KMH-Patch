using System;
using KMHPatch.Features.Roadworks.Dto;

namespace KMHPatch.Features.Roadworks
{
    // The caller's own projects, escrow and pricing. Read by the UI and the wealth ledger.
    public static class RoadworksCache
    {
        public static RoadworksSnapshot Snapshot       { get; private set; }
        public static DateTime          LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(RoadworksSnapshot snapshot)
        {
            if (snapshot == null) return;
            // Two transports can deliver out of order, so a late revision 19 must not undo 20; Clear() on disconnect lets a new server's lower revision apply.
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Roadworks");
            // Handlers run off the main thread and roads are Verse state; without marshalling, this waits for a world tick that never comes on a paused map.
            Diagnostics.KmhMainThread.Post(() => WorldComponent_KMHRoads.TryReconcile(0f));
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
            Diagnostics.KmhMainThread.Post(WorldComponent_KMHRoads.ForgetApplied);
        }
    }
}
