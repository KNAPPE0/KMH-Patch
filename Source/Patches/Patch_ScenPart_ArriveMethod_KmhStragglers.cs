using System;
using System.Collections.Generic;
using HarmonyLib;
using KMHPatch.Diagnostics;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // A gravship start places colonists only on its own substructure; RoM's Worlds Collide lays none, so the colony began empty.
    [HarmonyPatch(typeof(ScenPart_PlayerPawnsArriveMethod), nameof(ScenPart_PlayerPawnsArriveMethod.GenerateIntoMap))]
    internal static class Patch_ScenPart_ArriveMethod_KmhStragglers
    {
        [HarmonyPostfix]
        private static void Postfix(Map map)
        {
            try
            {
                List<Pawn> left = Unplaced(Find.GameInitData);
                if (map == null || left.Count == 0) return;
                IntVec3 near = MapGenerator.PlayerStartSpotValid ? MapGenerator.PlayerStartSpot : map.Center;
                foreach (Pawn p in left)
                {
                    if (!CellFinder.TryFindRandomCellNear(near, map, 12, c => c.Standable(map), out IntVec3 cell)
                        && !CellFinder.TryFindRandomCellNear(map.Center, map, 40, c => c.Standable(map), out cell)) continue;
                    GenSpawn.Spawn(p, cell, map);
                    KmhLog.Warn($"Start: the scenario found no place for {p.LabelShort}, so they were set down at {cell}.");
                }
            }
            catch (Exception ex) { KmhLog.Warn($"Start: setting down colonists the scenario left out failed: {ex.Message}"); }
        }

        // Starting colonists only; one held by a drop pod or anything else is still on its way.
        internal static List<Pawn> Unplaced(GameInitData init)
        {
            var left = new List<Pawn>();
            if (init?.startingAndOptionalPawns == null) return left;
            int n = Math.Min(init.startingPawnCount, init.startingAndOptionalPawns.Count);
            for (int i = 0; i < n; i++)
            {
                Pawn p = init.startingAndOptionalPawns[i];
                if (p != null && !p.Destroyed && !p.Spawned && p.holdingOwner == null) left.Add(p);
            }
            return left;
        }
    }
}
