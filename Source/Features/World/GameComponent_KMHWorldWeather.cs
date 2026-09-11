using System;
using System.Collections.Generic;
using KMHPatch.Features.World.Dto;
using RimWorld;
using Verse;

namespace KMHPatch.Features.World
{
    // Applies active world_weather events as GameConditions on player maps; the server's end time is the only authority.
    public class GameComponent_KMHWorldWeather : GameComponent
    {
        private const int SyncEveryTicks       = 250;
        private const int ReapplyCooldownTicks = 2500;      // ~1 in-game hour; churn guard for continuous conditions
        private const int OpenEndedTicks       = 3_600_000; // far beyond any server event; KMH decides the real end
        private const int MaxApplyAttempts     = 3;         // a def RimWorld refuses to keep must not be retried forever

        private enum State { Pending, Applied, EndedNaturally }

        private class Managed : IExposable
        {
            public int    MapId;
            public long   EventId;
            public string DefName;
            public State  St;
            public int    CooldownUntilTick;
            public long   EndsUtcTicks;   // last known server end; expiry still works with no snapshot
            public int    Attempts;       // applies since re-arm; capped so a rejected def stops churning

            public void ExposeData()
            {
                Scribe_Values.Look(ref MapId,             "mapId",             0);
                Scribe_Values.Look(ref EventId,           "eventId",           0L);
                Scribe_Values.Look(ref DefName,           "defName",           "");
                Scribe_Values.Look(ref St,                "state",             State.Pending);
                Scribe_Values.Look(ref CooldownUntilTick, "cooldownUntilTick", 0);
                Scribe_Values.Look(ref EndsUtcTicks,      "endsUtcTicks",      0L);
                Scribe_Values.Look(ref Attempts,          "attempts",          0);
            }
        }

        private List<Managed> _managed = new List<Managed>();

        // Session-only: one "definition unavailable" line per def instead of one per sync.
        private readonly HashSet<string> _missingDefsLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public GameComponent_KMHWorldWeather(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _managed, "kmhManagedConditions", LookMode.Deep);
            if (_managed == null) _managed = new List<Managed>();
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % SyncEveryTicks != 0) return;
            try { Sync(); }
            catch (Exception ex) { Diagnostics.KmhLog.Debug($"World weather sync: {ex.Message}"); }
        }

        private void Sync()
        {
            List<Map> maps = Find.Maps;
            if (maps == null) return;

            WorldSnapshot snap   = WorldCache.Snapshot;
            long          nowUtc = DateTime.UtcNow.Ticks;

            List<WorldEventDto> active = new List<WorldEventDto>();
            if (snap?.Events != null)
                foreach (WorldEventDto e in snap.Events)
                    if (e != null
                        && string.Equals(e.Type, WorldEventDto.WorldWeather, StringComparison.OrdinalIgnoreCase)
                        && e.EndsUtcTicks > nowUtc            // server clock decides, not RimWorld ticks
                        && !string.IsNullOrEmpty(e.Target))
                        active.Add(e);

            // Apply only from real server truth; expiry below still runs without a snapshot.
            if (snap != null)
                foreach (Map map in maps)
                {
                    if (map?.IsPlayerHome != true) continue;
                    foreach (WorldEventDto e in active)
                    {
                        GameConditionDef def = DefDatabase<GameConditionDef>.GetNamedSilentFail(e.Target);
                        if (def == null)
                        {
                            if (_missingDefsLogged.Add(e.Target))
                                Diagnostics.KmhLog.Debug($"World weather: {e.Target} unavailable (this client has no such GameConditionDef)");
                            continue;
                        }
                        Step(map, e, def);
                    }
                }

            Retire(maps, active, snap != null, nowUtc);
        }

        // Drive one (map, event, def) through Pending -> Applied -> EndedNaturally. Logs only on a real transition.
        private void Step(Map map, WorldEventDto e, GameConditionDef def)
        {
            Managed m = Track(map.uniqueID, e.Id, def.defName);
            m.EndsUtcTicks = e.EndsUtcTicks;   // keep fresh so an extended (or shortened) event is honoured

            bool isActive   = map.gameConditionManager.ConditionIsActive(def);
            bool applicable = IsApplicableNow(def, map);
            int  now        = Find.TickManager.TicksGame;

            if (m.St == State.Applied)
            {
                if (!applicable)   // Aurora at daybreak: end ours once, then wait for the next valid night
                {
                    if (isActive) EndOurs(map, def);
                    m.St = State.Pending;
                    Diagnostics.KmhLog.Debug($"World weather: {def.defName} ended (no longer applicable)");
                }
                else if (!isActive)   // RimWorld or another mod ended it while it was still valid
                {
                    m.St = State.EndedNaturally;
                    m.CooldownUntilTick = now + ReapplyCooldownTicks;
                    Diagnostics.KmhLog.Debug(m.Attempts >= MaxApplyAttempts
                        ? $"World weather: {def.defName} naturally ended {m.Attempts}x - not retrying this window"
                        : $"World weather: {def.defName} naturally ended");
                }
                return;
            }

            if (isActive) return;   // a condition we did not apply is running - never claim or end someone else's

            if (!applicable)
            {
                if (m.St != State.Pending)
                {
                    // Re-arm: each night is a legitimate new Aurora, so it gets a fresh attempt budget.
                    m.St = State.Pending;
                    m.Attempts = 0;
                    Diagnostics.KmhLog.Debug($"World weather: {def.defName} re-armed (waiting for a valid window)");
                }
                return;
            }

            if (m.St == State.EndedNaturally)
            {
                // Night-gated: stays ended for the rest of THIS night; only daylight re-arms it.
                if (IsNightGated(def)) return;
                if (now < m.CooldownUntilTick) return;
            }

            if (m.Attempts >= MaxApplyAttempts) return;   // gave up on this window; logged once below

            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(def, OpenEndedTicks));
            m.St = State.Applied;
            m.Attempts++;
            Diagnostics.KmhLog.Debug($"World weather: {def.defName} applied (ends {RemainingReal(m.EndsUtcTicks)})");
        }

        // Ends ONLY what we applied; with no snapshot it falls back to the last known end so a dropped link never cancels early.
        private void Retire(List<Map> maps, List<WorldEventDto> active, bool haveSnapshot, long nowUtc)
        {
            for (int i = _managed.Count - 1; i >= 0; i--)
            {
                Managed m = _managed[i];

                // Map abandoned: its uniqueID never returns, so the record would sit here for the life of the save.
                Map map = MapById(maps, m.MapId);
                if (map == null) { _managed.RemoveAt(i); continue; }

                bool keep = haveSnapshot ? StillActive(active, m) : m.EndsUtcTicks > nowUtc;
                if (keep) continue;

                if (m.St == State.Applied)
                {
                    GameConditionDef def = DefDatabase<GameConditionDef>.GetNamedSilentFail(m.DefName);
                    if (def != null && map.gameConditionManager.ConditionIsActive(def))
                    {
                        EndOurs(map, def);
                        Diagnostics.KmhLog.Debug($"World weather: {m.DefName} removed (server event ended)");
                    }
                }
                _managed.RemoveAt(i);
            }
        }

        private static bool StillActive(List<WorldEventDto> active, Managed m)
        {
            foreach (WorldEventDto e in active)
                if (e.Id == m.EventId && string.Equals(e.Target, m.DefName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static Map MapById(List<Map> maps, int id)
        {
            foreach (Map map in maps) if (map != null && map.uniqueID == id) return map;
            return null;
        }

        private Managed Track(int mapId, long eventId, string defName)
        {
            foreach (Managed m in _managed)
                if (m.MapId == mapId && m.EventId == eventId
                    && string.Equals(m.DefName, defName, StringComparison.OrdinalIgnoreCase))
                    return m;
            Managed created = new Managed { MapId = mapId, EventId = eventId, DefName = defName, St = State.Pending };
            _managed.Add(created);
            Diagnostics.KmhLog.Debug($"World weather: {defName} detected (map {mapId})");
            return created;
        }

        private static void EndOurs(Map map, GameConditionDef def)
            => map.gameConditionManager.GetActiveCondition(def)?.End();

        // Aurora only reads as one at night and RimWorld ends it at dawn; applying it in daylight made it thrash.
        private static bool IsNightGated(GameConditionDef def)
            => string.Equals(def.defName, "Aurora", StringComparison.OrdinalIgnoreCase);

        private static bool IsApplicableNow(GameConditionDef def, Map map)
            => !IsNightGated(def) || IsNight(map);

        // Celestial glow, not sky glow: sky glow is darkened by an eclipse and would read as "night".
        private static bool IsNight(Map map) => GenCelestial.CurCelestialSunGlow(map) < 0.01f;

        private static string RemainingReal(long endsUtcTicks)
        {
            TimeSpan left = TimeSpan.FromTicks(Math.Max(0L, endsUtcTicks - DateTime.UtcNow.Ticks));
            return left.TotalHours >= 1 ? $"in {(int)left.TotalHours}h {left.Minutes}m" : $"in {(int)left.TotalMinutes}m";
        }
    }
}
