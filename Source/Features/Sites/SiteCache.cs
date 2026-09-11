using System;
using KMHPatch.Features.Sites.Dto;

namespace KMHPatch.Features.Sites
{
    // client cache for the latest custom-sites snapshot
    public static class SiteCache
    {
        public static SiteSnapshot Snapshot       { get; private set; }
        public static DateTime     LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(SiteSnapshot snapshot)
        {
            // Two transports can deliver out of order; Clear() on disconnect lets a new server's lower revision apply.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Site");
            // Handlers run off the main thread and WorldObjects are main-thread only; a paused map never ticks.
            Diagnostics.KmhMainThread.Post(() => WorldComponent_KMHSiteMarkers.TryReconcile(0f));
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
