using HarmonyLib;
using RimWorld;

namespace KMHPatch.Patches
{
    // A colonist held inside a KMH site is on no map or caravan, so RimWorld's scan would wrongly declare the colony lost.
    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    internal static class Patch_GameEnder_KmhHeldWorkers
    {
        [HarmonyPrefix]
        private static bool Prefix(GameEnder __instance)
        {
            try
            {
                var holder = Features.Sites.WorldComponent_KMHSiteWorkers.Instance;
                if (holder != null && holder.HasLiveHeldColonist)
                {
                    __instance.gameEnding = false;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
