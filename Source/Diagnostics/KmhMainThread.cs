using System;
using System.Collections.Concurrent;
using Verse;

namespace KMHPatch.Diagnostics
{
    // The transport's TCP threads cannot touch Verse.Log, Messages or Find.* directly.
    internal static class KmhMainThread
    {
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public static void Post(Action action)
        {
            if (action == null) return;
            bool onMain;
            // UnityData cannot answer before the game is up or after it is gone, and a transport thread must not die over that.
            try { onMain = UnityData.IsInMainThread; }
            catch { _queue.Enqueue(action); return; }
            if (onMain) Run(action);   // already on main
            else _queue.Enqueue(action);
        }

        public static void Pump()
        {
            while (_queue.TryDequeue(out Action action)) Run(action);
        }

        // Test seam standing in for one call site's generation re-check, proving the queue drops work from an ended session.
        internal static void PostForTest(int generation, Action action)
            => _queue.Enqueue(() => { if (generation == SubProtocol.KmhDispatcher.SessionGeneration) Run(action); });

        internal static void PumpForTest() => Pump();

        private static void Run(Action action)
        {
            try { action(); } catch { /* never recurse into logging from here */ }
        }
    }
}
