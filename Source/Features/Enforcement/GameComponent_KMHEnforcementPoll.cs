using System;
using System.Diagnostics;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // ~60s still catches a mid-session op/de-op; polling faster is measurable server noise for a rare event.
    public class GameComponent_KMHEnforcementPoll : GameComponent
    {
        internal const double IntervalSeconds = 60;
        private long _lastAsked;

        public GameComponent_KMHEnforcementPoll(Game game) { }

        // Real seconds, not ticks, which game speed multiplies. Any snapshot that arrived counts as asked.
        internal static bool Due(long now, long lastAsked, long lastSnapshot, long ticksPerSecond)
            => now - Math.Max(lastAsked, lastSnapshot) >= (long)(IntervalSeconds * ticksPerSecond);

        public override void GameComponentUpdate()
        {
            if (!KmhDispatcher.IsKmhServer) return;
            long now = Stopwatch.GetTimestamp();
            if (!Due(now, _lastAsked, EnforcementHandler.LastSnapshotStamp, Stopwatch.Frequency)) return;
            _lastAsked = now;
            try { EnforcementHandler.RequestSnapshot(); }
            catch (Exception ex) { KmhLog.Warn($"Enforcement poll failed: {ex.Message}"); }
        }
    }
}
