using HarmonyLib;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // Kill tally by defName feeding hunt auto-verify, keyed by kind and race defName. Counts only kills the PLAYER'S
    // side caused (hunting/combat/traps/turrets) - NOT natural deaths, predator kills, or another faction's kills, so
    // a thrumbo dying in a raid can't complete your "hunt thrumbo" quest. Stored in GameComponent_KMHKillTally so it
    // survives save/reload. The server still accepts client-reported completion; this just saves the manual Report click
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class Patch_Pawn_Kill_HuntTally
    {
        public static int KillsOf(string defName)
            => GameComponent_KMHKillTally.Current?.KillsOf(defName) ?? 0;

        [HarmonyPostfix]
        private static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                if (__instance == null || !__instance.Dead) return;
                if (__instance.Faction == Faction.OfPlayer)
                {
                    // Our own humanlike (colonist/slave) died - count it as a colony loss for Battle Records.
                    if (__instance.RaceProps?.Humanlike == true)
                        GameComponent_KMHKillTally.Current?.BumpDeath();
                    return;                                                  // not an enemy kill
                }
                if (dinfo?.Instigator?.Faction != Faction.OfPlayer) return;  // only player-caused kills count

                GameComponent_KMHKillTally tally = GameComponent_KMHKillTally.Current;
                if (tally == null) return;
                tally.Bump(__instance.kindDef?.defName);
                tally.Bump(__instance.def?.defName);
            }
            catch { /* a tally hiccup must never disturb the death pipeline */ }
        }
    }
}
