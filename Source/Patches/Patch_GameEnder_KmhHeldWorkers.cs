using HarmonyLib;
using RimWorld;

namespace KMHPatch.Patches
{
    // A colonist pulled "inside" a KMH site is despawned into our own container (frozen, on no map or caravan), so
    // RimWorld's colonist scan can't see them and could wrongly conclude you lost your whole colony. While we hold at
    // least one live colonist, skip the check and cancel any pending game-over countdown - they're alive, just away;
    // recalling restores normal behavior.
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
                    __instance.gameEnding = false;   // not game over - a colonist is alive inside a site
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
