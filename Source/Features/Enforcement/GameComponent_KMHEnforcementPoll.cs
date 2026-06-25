using System;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Polls enforcement while connected so mid-session op/de-op changes are caught within ~20s.
    public class GameComponent_KMHEnforcementPoll : GameComponent
    {
        private const int IntervalTicks = 1200; // ~20s at 1x (60 TPS)
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
