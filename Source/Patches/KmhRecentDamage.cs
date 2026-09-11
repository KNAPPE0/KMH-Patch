using System.Collections.Generic;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // A hunt target that bleeds out after a colonist hurt it still counts; a predator or disease death does not.
    internal static class KmhRecentDamage
    {
        private const int AttributionWindowTicks = 60000;  // ~1 in-game day (GlobalQuestKillAttributionWindowTicks)
        private const int MinDamageForAttribution = 1;     // (GlobalQuestMinDamageForAttribution)
        private const int MaxTracked = 512;                // bound memory; prune oldest when exceeded

        // pawn.thingIDNumber -> last game tick a player-faction source damaged it.
        private static readonly Dictionary<int, int> _lastPlayerDamageTick = new Dictionary<int, int>();

        public static void Record(Pawn victim, DamageInfo dinfo)
        {
            if (victim == null || victim.Dead) return;
            if (dinfo.Amount < MinDamageForAttribution) return;
            Thing inst = dinfo.Instigator;
            if (inst == null || inst.Faction != Faction.OfPlayer) return;   // only player-caused damage attributes
            int now = CurTick();
            _lastPlayerDamageTick[victim.thingIDNumber] = now;
            if (_lastPlayerDamageTick.Count > MaxTracked) Prune(now);
        }

        public static bool PlayerDamagedRecently(Pawn victim)
        {
            if (victim == null) return false;
            if (!_lastPlayerDamageTick.TryGetValue(victim.thingIDNumber, out int tick)) return false;
            return CurTick() - tick <= AttributionWindowTicks;
        }

        public static void Forget(Pawn victim)
        {
            if (victim != null) _lastPlayerDamageTick.Remove(victim.thingIDNumber);
        }

        private static int CurTick()
        {
            try { return Find.TickManager?.TicksGame ?? 0; } catch { return 0; }
        }

        private static void Prune(int now)
        {
            List<int> drop = null;
            foreach (KeyValuePair<int, int> kv in _lastPlayerDamageTick)
                if (now - kv.Value > AttributionWindowTicks) (drop ?? (drop = new List<int>())).Add(kv.Key);
            if (drop != null) foreach (int k in drop) _lastPlayerDamageTick.Remove(k);
            // Still too big (many recent entries)? Drop arbitrary ones - correctness is best-effort, memory is bounded.
            if (_lastPlayerDamageTick.Count > MaxTracked)
            {
                drop = new List<int>();
                foreach (int k in _lastPlayerDamageTick.Keys) { drop.Add(k); if (drop.Count >= _lastPlayerDamageTick.Count - MaxTracked + 1) break; }
                foreach (int k in drop) _lastPlayerDamageTick.Remove(k);
            }
        }
    }
}
