using System;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Enforcement;
using KMHPatch.Features.PlayerStats;
using KMHPatch.Features.Quests;
using KMHPatch.Features.World;
using Verse;

namespace KMHPatch.Patches
{
    // Ensure KMH GameComponents exist after payload load, without replacing saved components or their data.
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    internal static class Patch_Game_FillComponents_KmhComponents
    {
        [HarmonyPostfix]
        private static void Postfix(Game __instance)
        {
            Ensure<GameComponent_KMHColonyReporter>(__instance);
            Ensure<WorldQuestReporter>(__instance);
            Ensure<QuestAutoVerify>(__instance);
            Ensure<GameComponent_KMHKillTally>(__instance);
            Ensure<GameComponent_KMHEnforcementPoll>(__instance);

            // Site markers live on a WorldComponent, which RimWorld only auto-instantiates at world build - on first
            // join the payload can load after that, so the marker component is missing until a rejoin rebuilds the
            // world (markers then only appeared on rejoin). Ensure it here too, exactly like the GameComponents.
            EnsureWorld<Features.Sites.WorldComponent_KMHSiteMarkers>();
        }

        private static void Ensure<T>(Game game) where T : GameComponent
        {
            try
            {
                if (game.GetComponent<T>() != null) return;
                game.components.Add((GameComponent)Activator.CreateInstance(typeof(T), game));
                KmhLog.Debug($"KMH: registered missing GameComponent {typeof(T).Name}.");
            }
            catch (Exception ex) { KmhLog.Warn($"KMH: could not add {typeof(T).Name}: {ex.Message}"); }
        }

        // RimWorld.Planet.World fully qualified - the KMHPatch.Features.World namespace shadows the bare 'World' type.
        private static void EnsureWorld<T>() where T : RimWorld.Planet.WorldComponent
        {
            try
            {
                RimWorld.Planet.World world = Find.World;
                if (world == null || world.GetComponent<T>() != null) return;
                world.components.Add((RimWorld.Planet.WorldComponent)Activator.CreateInstance(typeof(T), world));
                KmhLog.Debug($"KMH: registered missing WorldComponent {typeof(T).Name}.");
            }
            catch (Exception ex) { KmhLog.Warn($"KMH: could not add {typeof(T).Name}: {ex.Message}"); }
        }
    }
}
