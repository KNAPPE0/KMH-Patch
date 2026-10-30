using HarmonyLib;
using KMHPatch.Features.PlayerStats;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class Patch_Pawn_Kill_HuntTally
    {
        public static int KillsOf(string defName)
            => GameComponent_KMHKillTally.Current?.KillsOf(defName) ?? 0;

        // Read while alive: only a living counted colonist is a colony loss, so a second Kill on a corpse is not one.
        [HarmonyPrefix]
        private static void Prefix(Pawn __instance, out bool __state)
        {
            __state = false;
            try { __state = GameComponent_KMHColonyReporter.IsCountedColonist(__instance); }
            catch { }
        }

        [HarmonyPostfix]
        private static void Postfix(Pawn __instance, DamageInfo? dinfo, bool __state)
        {
            try
            {
                if (__instance == null || !__instance.Dead) return;
                GameComponent_KMHKillTally tally = GameComponent_KMHKillTally.Current;
                int tick = Find.TickManager?.TicksGame ?? 0;
                NoteKill(tally, dinfo?.Instigator as Pawn, __instance, tick);
                if (__state) tally?.RecordFallen(GameComponent_KMHKillTally.FallenFrom(__instance, tick));
                if (__instance.Faction == Faction.OfPlayer) return;
                // A thrumbo dying in a raid must not complete a hunt quest, so the player has to have struck or recently damaged it.
                bool playerBlow   = dinfo?.Instigator?.Faction == Faction.OfPlayer;
                bool playerRecent = KmhRecentDamage.PlayerDamagedRecently(__instance);
                if (!playerBlow && !playerRecent) return;

                if (tally == null) return;
                // For animals kindDef.defName equals def.defName, so bumping both would count one kill twice.
                string kind = __instance.kindDef?.defName;
                string race = __instance.def?.defName;
                tally.Bump(kind);
                if (!string.IsNullOrEmpty(race) && !string.Equals(race, kind, System.StringComparison.OrdinalIgnoreCase))
                    tally.Bump(race);
                KmhRecentDamage.Forget(__instance);
            }
            catch { /* a tally hiccup must never disturb the death pipeline */ }
        }

        // The colonist profile's recent combat: only what the game itself credits as this colonist's kill.
        private static void NoteKill(GameComponent_KMHKillTally tally, Pawn killer, Pawn victim, int tick)
        {
            if (tally == null || killer == null || killer == victim) return;
            if (!GameComponent_KMHColonyReporter.IsCountedColonist(killer)) return;
            string label = victim.LabelShortCap;
            string kind = victim.kindDef?.label;
            if (victim.RaceProps?.Humanlike == true && !string.IsNullOrEmpty(kind) && !string.Equals(kind, label, System.StringComparison.OrdinalIgnoreCase))
                label = $"{label} ({kind})";
            tally.NoteCombat(killer.ThingID, $"Killed {label} · day {tick / GenDate.TicksPerDay + 1}", tick);
        }
    }
}
