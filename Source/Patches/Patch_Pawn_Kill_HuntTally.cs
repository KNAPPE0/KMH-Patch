using HarmonyLib;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
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
                    // A colony loss for Battle Records.
                    if (__instance.RaceProps?.Humanlike == true)
                        GameComponent_KMHKillTally.Current?.BumpDeath();
                    return;
                }
                // A thrumbo dying in a raid must not complete a hunt quest, so the player has to have struck or recently damaged it.
                bool playerBlow   = dinfo?.Instigator?.Faction == Faction.OfPlayer;
                bool playerRecent = KmhRecentDamage.PlayerDamagedRecently(__instance);
                if (!playerBlow && !playerRecent) return;

                GameComponent_KMHKillTally tally = GameComponent_KMHKillTally.Current;
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
    }
}
