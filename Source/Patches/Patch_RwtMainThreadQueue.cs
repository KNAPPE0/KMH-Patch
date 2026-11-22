using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;

namespace KMHPatch.Patches
{
    // RWT runs each queued job inside its queue lock, so a long save (one per road piece) stops its network thread and the server drops the player at 60 s.
    internal static class Patch_RwtMainThreadQueue
    {
        private static Func<Queue<Action>> _queue;

        internal static bool Applied { get; private set; }

        internal static MethodInfo Target { get; private set; }

        public static void TryApply(Harmony harmony)
        {
            try
            {
                string ns = RwtCompat.ClientNsRoot;
                Type manager = RwtCompat.ResolveType(ns, "MainThreadManager", ns + ".Managers.MainThreadManager");
                MethodInfo run = manager == null ? null : AccessTools.Method(manager, "ExecuteAllQueue", Type.EmptyTypes);
                MethodInfo get = manager == null ? null : AccessTools.PropertyGetter(manager, "ActionQueue");
                if (run == null || get == null || !get.IsStatic || get.ReturnType != typeof(Queue<Action>))
                {
                    KmhLog.Info("RWT job queue: this RWT build has no such queue - left as it is.");
                    return;
                }
                _queue = (Func<Queue<Action>>)Delegate.CreateDelegate(typeof(Func<Queue<Action>>), get);
                harmony.Patch(run, prefix: new HarmonyMethod(typeof(Patch_RwtMainThreadQueue)
                    .GetMethod(nameof(RunOutsideLock), BindingFlags.NonPublic | BindingFlags.Static)));
                Target = run;
                Applied = true;
                KmhLog.Info("RWT job queue: jobs run outside the queue lock, so a long save no longer stalls the connection.");
            }
            catch (Exception ex) { KmhLog.Warn($"RWT job queue setup threw: {ex.Message}"); }
        }

        // RWT's order; a pass takes only the jobs queued when it starts, so a busy connection cannot keep it running.
        private static bool RunOutsideLock()
        {
            Queue<Action> q = _queue?.Invoke();
            if (q == null) return true;
            int jobs;
            lock (q) jobs = q.Count;
            while (jobs-- > 0)
            {
                Action next;
                lock (q)
                {
                    if (q.Count == 0) break;
                    next = q.Dequeue();
                }
                next();
            }
            return false;
        }
    }
}
