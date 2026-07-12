using System;
using KMHPatch.Diagnostics;

namespace KMHPatch
{
    // One safe way for a client cache to fan out its Updated event: invoke subscribers, but never let a bad listener
    // break the snapshot apply. Every *Cache.Apply used to inline the same try/catch - they now call this so the
    // behaviour (and the log wording) lives in one place. Pass the event's current value: Raise(Updated, "Auction").
    internal static class KmhCacheEvents
    {
        public static void Raise(Action updated, string cacheName)
        {
            try { updated?.Invoke(); }
            catch (Exception ex) { KmhLog.Warn($"{cacheName} cache subscriber threw: {ex.Message}"); }
        }
    }
}
