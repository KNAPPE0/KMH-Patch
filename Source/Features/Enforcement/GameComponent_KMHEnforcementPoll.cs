using System;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // ~60s so mid-session op/de-op is caught; 20s was measurable server noise for a rare event.
    public class GameComponent_KMHEnforcementPoll : GameComponent
    {
        private const int IntervalTicks = 3600; // ~60s at 1x (60 TPS)
        private int _next;

        public GameComponent_KMHEnforcementPoll(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame < _next) return;
            _next = Find.TickManager.TicksGame + IntervalTicks;
            try { if (KmhDispatcher.IsKmhServer) EnforcementHandler.RequestSnapshot(); }
            catch (Exception ex) { KmhLog.Warn($"Enforcement poll failed: {ex.Message}"); }
        }
    }
}
