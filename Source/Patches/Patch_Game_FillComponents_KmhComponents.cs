using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Patches
{
    // RimWorld scanned component types before the payload loaded, so KMH's are added here; saved ones are never replaced.
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    internal static class Patch_Game_FillComponents_KmhComponents
    {
        // Derived, never hand-listed: a hand list missed the delivery receipts, so owed deliveries landed again every join.
        internal static Type[] GameComponentTypes() => ComponentTypes(typeof(GameComponent));
        internal static Type[] WorldComponentTypes() => ComponentTypes(typeof(RimWorld.Planet.WorldComponent));

        private static Type[] ComponentTypes(Type baseType)
        {
            Type[] all;
            try { all = typeof(Patch_Game_FillComponents_KmhComponents).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(t => t != null).ToArray(); }
            return all.Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t))
                      .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        }

        [HarmonyPostfix]
        private static void Postfix(Game __instance)
        {
            foreach (Type t in GameComponentTypes()) Ensure(__instance, t);
            // RimWorld auto-instantiates WorldComponents only at world build, which the payload can load after.
            foreach (Type t in WorldComponentTypes()) EnsureWorld(t);
        }

        private static void Ensure(Game game, Type t)
        {
            try
            {
                if (game.components.Any(c => c != null && c.GetType() == t)) return;
                game.components.Add((GameComponent)Activator.CreateInstance(t, game));
                KmhLog.Debug($"KMH: registered missing GameComponent {t.Name}.");
            }
            catch (Exception ex) { KmhLog.Warn($"KMH: could not add {t.Name}: {ex.Message}"); }
        }

        // RimWorld.Planet.World fully qualified - the KMHPatch.Features.World namespace shadows the bare 'World' type.
        private static void EnsureWorld(Type t)
        {
            try
            {
                RimWorld.Planet.World world = Find.World;
                if (world == null || world.components.Any(c => c != null && c.GetType() == t)) return;
                world.components.Add((RimWorld.Planet.WorldComponent)Activator.CreateInstance(t, world));
                KmhLog.Debug($"KMH: registered missing WorldComponent {t.Name}.");
            }
            catch (Exception ex) { KmhLog.Warn($"KMH: could not add {t.Name}: {ex.Message}"); }
        }
    }
}
