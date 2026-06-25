using System;
using System.Collections.Concurrent;
using Verse;

namespace KMHPatch.Diagnostics
{
    // Marshals work onto the main thread - the API transport's TCP threads can't touch Verse.Log/Messages/Find.*
    // directly. Posted here, drained each frame by Patch_Root_Update_KmhPump.
    internal static class KmhMainThread
    {
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public static void Post(Action action)
        {
            if (action == null) return;
            if (UnityData.IsInMainThread) Run(action);   // already on main
            else _queue.Enqueue(action);
        }

        public static void Pump()
        {
            while (_queue.TryDequeue(out Action action)) Run(action);
        }

        private static void Run(Action action)
        {
            try { action(); } catch { /* never recurse into logging from here */ }
        }
    }
}
