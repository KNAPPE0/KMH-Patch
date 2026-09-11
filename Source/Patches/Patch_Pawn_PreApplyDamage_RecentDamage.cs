using HarmonyLib;
using Verse;

namespace KMHPatch.Patches
{
    // Postfix so a fully absorbed hit, such as one a shield ate, can be skipped.
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
