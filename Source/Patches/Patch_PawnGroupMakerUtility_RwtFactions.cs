using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KMHPatch.Features.Mods;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // RWT factions own work sites but no workers: vanilla's result (no pawns) without its per-join error log.
    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.GeneratePawnKindsExample))]
    internal static class Patch_PawnGroupMakerUtility_RwtFactions
    {
        [HarmonyPrefix]
        private static bool Prefix(PawnGroupMakerParms parms, ref IEnumerable<PawnKindDef> __result)
        {
            if (!NoMakerOfKind(parms?.faction?.def, parms?.groupKind)) return true;
            __result = Enumerable.Empty<PawnKindDef>();
            return false;
        }

        // Only a RimWorld Together faction, and only for a kind it has no maker for at all; anything else is vanilla's call.
        internal static bool NoMakerOfKind(FactionDef def, PawnGroupKindDef kind)
        {
            if (def == null || kind == null || !IsRwtDef(def)) return false;
            if (def.pawnGroupMakers != null)
                foreach (PawnGroupMaker m in def.pawnGroupMakers) if (m?.kindDef == kind) return false;
            return true;
        }

        private static bool IsRwtDef(Def def)
            => string.Equals(KmhModPlan.Bare(def.modContentPack?.PackageId ?? ""), KmhModPlan.RwtId, StringComparison.OrdinalIgnoreCase);
    }
}
