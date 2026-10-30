using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Global
{
    // One registry, not hand-kept lists: those drifted and leaked the previous account's chats after a relink.
    internal static class KmhIdentityTeardown
    {
        private static readonly List<(string Name, Action Forget)> Registered = new List<(string, Action)>();

        public static void Register(string name, Action forget)
        {
            if (forget == null) return;
            lock (Registered) Registered.Add((name ?? "", forget));
        }

        public static void ForgetIdentity()
        {
            // First, so a poll already in flight is refused even if the caches below throw on the way out.
            KmhIdentityGeneration.Bump();

            List<(string Name, Action Forget)> all;
            lock (Registered) all = new List<(string, Action)>(Registered);

            // One failure must not leave the rest of the previous identity loaded.
            foreach ((string name, Action forget) in all)
            {
                try { forget(); }
                catch (Exception ex) { KmhLog.Debug($"Identity teardown: {name} failed: {ex.Message}"); }
            }
        }

        internal static int RegisteredCount { get { lock (Registered) return Registered.Count; } }
    }
}
