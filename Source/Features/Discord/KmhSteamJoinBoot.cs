using System;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Discord
{
    // Not at load: Steamworks is not initialised yet and the directory must exist first, so this rides the frame pump.
    internal static class KmhSteamJoinBoot
    {
        private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);
        // Gives up eventually so this never becomes a permanent per-frame cost.
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

        private static DateTime _next = DateTime.MinValue;
        private static DateTime _deadline = DateTime.MinValue;
        private static bool _done;

        public static void Tick()
        {
            if (_done) return;
            DateTime now = DateTime.UtcNow;
            if (_deadline == DateTime.MinValue) _deadline = now + GiveUpAfter;
            if (now < _next) return;
            _next = now + Retry;

            try
            {
                KmhSteamJoinCallback.Register();
                if (KmhSteamJoinCallback.Registered)
                {
                    // Only once the callback is live, so a launch join cannot pre-empt a newer one from Steam.
                    KmhSteamJoin.HandleStartup();
                    _done = true;
                    return;
                }
            }
            catch (Exception ex) { KmhLog.Debug($"Steam: Join Game setup failed ({ex.GetType().Name})."); }

            if (now >= _deadline)
            {
                _done = true;
                // A launch argument is still worth answering even where the live callback never came up.
                try { KmhSteamJoin.HandleStartup(); } catch (Exception) { }
                KmhLog.Debug("Steam: Join Game is not available in this session.");
            }
        }

        internal static void ResetForTest()
        {
            _done = false; _next = DateTime.MinValue; _deadline = DateTime.MinValue;
        }
    }
}
