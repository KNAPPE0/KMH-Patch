using System;
using System.Collections.Generic;
using KMHPatch.Features.World.Dto;
using RimWorld;
using Verse;

namespace KMHPatch.Features.World
{
    // Applies active world_weather events as self-expiring GameConditions on player maps; removes only what it added.
    public class GameComponent_KMHWorldWeather : GameComponent
    {
        private const int SyncEveryTicks = 250;

        // (map uniqueID, defName) pairs this component added - never touch naturally-occurring conditions.
        private readonly HashSet<string> _applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public GameComponent_KMHWorldWeather(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % SyncEveryTicks != 0) return;
            try { Sync(); }
            catch (Exception ex) { Diagnostics.KmhLog.Debug($"World weather sync: {ex.Message}"); }
        }

        private void Sync()
        {
            // Active weather defs from the server snapshot (empty when not on a KMH server).
            HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<WorldEventDto> events = WorldCache.Snapshot?.Events;
            long nowTicks = DateTime.UtcNow.Ticks;
            if (events != null)
                foreach (WorldEventDto e in events)
                    if (e != null
                        && string.Equals(e.Type, WorldEventDto.WorldWeather, StringComparison.OrdinalIgnoreCase)
                        && e.EndsUtcTicks > nowTicks
                        && !string.IsNullOrEmpty(e.Target))
                        wanted.Add(e.Target);

            List<Map> maps = Find.Maps;
            if (maps == null) return;

            foreach (Map map in maps)
            {
                if (map?.IsPlayerHome != true) continue;
                foreach (string defName in wanted)
                {
                    GameConditionDef def = DefDatabase<GameConditionDef>.GetNamedSilentFail(defName);
                    if (def == null) continue;   // unknown/modded def this client lacks - skip quietly
                    if (map.gameConditionManager.ConditionIsActive(def)) continue;

                    long remainUtc = LongestRemainingTicks(events, defName, nowTicks);
                    int  duration  = (int)Math.Min(Math.Max(remainUtc / TimeSpan.TicksPerSecond * 60L, 2500L), 3_600_000L);
                    GameCondition cond = GameConditionMaker.MakeCondition(def, duration);
                    map.gameConditionManager.RegisterCondition(cond);
                    _applied.Add(Key(map, defName));
                }
            }

            // End only conditions we added whose event is gone.
            List<string> stale = null;
            foreach (string key in _applied)
            {
                string defName = key.Substring(key.IndexOf('|') + 1);
                if (wanted.Contains(defName)) continue;
                (stale ?? (stale = new List<string>())).Add(key);
                foreach (Map map in maps)
                {
                    if (map == null || Key(map, defName) != key) continue;
                    GameConditionDef def = DefDatabase<GameConditionDef>.GetNamedSilentFail(defName);
                    GameCondition cond = def == null ? null : map.gameConditionManager.GetActiveCondition(def);
                    cond?.End();
                }
            }
            if (stale != null) foreach (string k in stale) _applied.Remove(k);
        }

        private static long LongestRemainingTicks(List<WorldEventDto> events, string defName, long nowTicks)
        {
            long best = 0;
            if (events != null)
                foreach (WorldEventDto e in events)
                    if (e != null && string.Equals(e.Target, defName, StringComparison.OrdinalIgnoreCase)
                        && e.EndsUtcTicks > nowTicks)
                        best = Math.Max(best, e.EndsUtcTicks - nowTicks);
            return best;
        }

        private static string Key(Map map, string defName) => map.uniqueID + "|" + defName;
    }
}
