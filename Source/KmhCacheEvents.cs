using System;
using KMHPatch.Diagnostics;

namespace KMHPatch
{
    // A throwing listener must never break the snapshot apply that raised it.
    internal static class KmhCacheEvents
    {
        private static long _generation;

        // Every cache apply passes through here, so one counter answers "has anything the UI draws from changed?".
        public static long Generation => System.Threading.Interlocked.Read(ref _generation);

        // For state the UI renders that reaches no cache - staff badges arrive on the hello, not in a snapshot.
        public static void Bump() => System.Threading.Interlocked.Increment(ref _generation);

        public static void Raise(Action updated, string cacheName)
        {
            Bump();
            try { updated?.Invoke(); }
            catch (Exception ex) { KmhLog.Warn($"{cacheName} cache subscriber threw: {ex.Message}"); }
        }
    }
}
