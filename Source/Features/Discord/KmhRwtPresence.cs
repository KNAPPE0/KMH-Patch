using System;
using System.Reflection;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Discord
{
    // RWT's StartPresence() builds its own client and StopPresence() disposes it; KMH drives both so exactly one
    // status is up, and turning KMH's off puts RWT's back.
    internal static class KmhRwtPresence
    {
        private static bool _resolved;
        private static MethodInfo _start, _stop;
        private static PropertyInfo _rpClient;
        private static bool _stoppedByKmh;

        internal static bool StoppedByKmh => _stoppedByKmh;

        internal static bool Available { get { Resolve(); return _start != null && _stop != null; } }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type t = Patches.Patch_DiscordManager_KmhPresence.ManagerType();
                if (t == null) return;
                const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Static | BindingFlags.Instance;
                _start = t.GetMethod("StartPresence", Any);
                _stop = t.GetMethod("StopPresence", Any);
                _rpClient = t.GetProperty("RPClient", Any);
            }
            catch (Exception ex) { KmhLog.Debug($"Discord: RWT's presence manager is not a shape KMH knows ({ex.GetType().Name})."); }
        }

        // RWT's StopPresence() disposes RPClient without a null check, so a presence it never started is left alone.
        public static void Suspend()
        {
            Resolve();
            if (_stoppedByKmh || _stop == null) return;
            try
            {
                if (_rpClient == null || _rpClient.GetValue(null) == null) return;
                _stop.Invoke(null, null);
                _stoppedByKmh = true;
                KmhLog.Debug("Discord: RWT's own presence paused while KMH's is up.");
            }
            catch (Exception ex) { KmhLog.Debug($"Discord: could not pause RWT's presence ({ex.GetType().Name})."); }
        }

        public static void Resume()
        {
            Resolve();
            if (!_stoppedByKmh || _start == null) return;
            // KMH's prefix would swallow the call and RWT would never get its status back.
            if (KmhRichPresence.Active) return;

            _stoppedByKmh = false;   // cleared first: a second StartPresence would leak a client
            try { _start.Invoke(null, null); KmhLog.Debug("Discord: RWT's own presence restored."); }
            catch (Exception ex) { KmhLog.Debug($"Discord: could not restore RWT's presence ({ex.GetType().Name})."); }
        }

        internal static void ResetForTest()
        {
            _resolved = false; _start = null; _stop = null; _rpClient = null; _stoppedByKmh = false;
        }
    }
}
