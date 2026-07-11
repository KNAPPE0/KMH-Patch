using HarmonyLib;
using Verse;

namespace KMHPatch.Patches
{
    // Records player-caused damage to pawns for hunt-quest bleed-out attribution (see KmhRecentDamage). Postfix so we
    // can skip fully-absorbed hits (shields), and it never disturbs the damage pipeline.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    internal static class Patch_Pawn_PreApplyDamage_RecentDamage
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn __instance, DamageInfo dinfo, bool absorbed)
        {
            try { if (!absorbed) KmhRecentDamage.Record(__instance, dinfo); }
            catch { /* attribution is best-effort - never disturb damage handling */ }
        }
    }
}
