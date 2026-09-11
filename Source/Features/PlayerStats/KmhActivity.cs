using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.PlayerStats
{
    // Playing, as opposed to leaving the game open. Reported in small pieces the server clamps to real time.
    internal static class KmhActivity
    {
        // Running gets far longer than paused: watching a raid play out without touching anything is still playing.
        internal const float IdlePausedSeconds  = 120f;
        internal const float IdleRunningSeconds = 600f;

        private const float ReportSeconds = 60f;
        private const float MouseMovedPixels = 4f;

        private static float _lastInputAt = -999f;
        private static float _countedAt;
        private static float _reportedAt;
        private static float _credit;
        private static Vector3 _mouseWas;

        // The rule itself, kept pure: paused or not, how long since the player last touched anything.
        internal static bool IsActive(bool paused, float sinceInput)
            => sinceInput <= (paused ? IdlePausedSeconds : IdleRunningSeconds);

        // No Unity call - the disconnect path may be off-main-thread and a throw would skip every later cache clear.
        public static void Reset()
        {
            _lastInputAt = -999f;
            _credit = 0f;
            _countedAt = _reportedAt = 0f;
        }

        // Polled from the KMH update pump, like everything else that has to watch the game rather than be called by it.
        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;

            // Single-player time belongs to nobody's server, and reporting it would only warn once a minute forever.
            if (!KmhDispatcher.IsKmhServer) { _credit = 0f; _countedAt = _reportedAt = now; return; }

            if (Touched()) _lastInputAt = now;

            float since = now - _countedAt;
            _countedAt = now;
            if (since > 0f && since < 5f && IsActive(Paused(), now - _lastInputAt)) _credit += since;

            if (now - _reportedAt < ReportSeconds) return;
            _reportedAt = now;

            int whole = Mathf.FloorToInt(_credit);
            if (whole <= 0) return;
            if (!KmhDispatcher.Send(KmhProtocol.Kind.PlayerActive, new { seconds = whole })) return;
            _credit -= whole;
        }

        private static bool Paused()
        {
            try { return Find.TickManager == null || Find.TickManager.Paused; }
            catch { return true; }
        }

        // Mouse movement counts, but only real movement: a resting mouse jitters by a pixel and would never idle.
        private static bool Touched()
        {
            try
            {
                if (Input.anyKey) return true;
                if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f) return true;
                Vector3 at = Input.mousePosition;
                bool moved = (at - _mouseWas).sqrMagnitude > MouseMovedPixels * MouseMovedPixels;
                if (moved) _mouseWas = at;
                return moved;
            }
            catch { return false; }
        }
    }
}
