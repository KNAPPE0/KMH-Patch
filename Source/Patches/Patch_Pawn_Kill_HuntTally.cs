using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // Session kill tally by defName feeding hunt auto-verify. Counts non-colonist deaths (wildlife + enemies), keyed
    // by kind and race defName. The server already accepts client-reported completion - this just saves the manual
    // Report click. Session-scoped, not saved
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class Patch_Pawn_Kill_HuntTally
    {
        private static readonly Dictionary<string, int> _kills =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static int KillsOf(string defName)
            => !string.IsNullOrEmpty(defName) && _kills.TryGetValue(defName, out int n) ? n : 0;

        [HarmonyPostfix]
        private static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance == null || !__instance.Dead) return;
                if (__instance.Faction == Faction.OfPlayer) return; // skip own colonists
                Bump(__instance.kindDef?.defName);
                Bump(__instance.def?.defName);
            }
            catch { /* a tally hiccup must never disturb the death pipeline */ }
        }

        private static void Bump(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _kills.TryGetValue(key, out int n);
            _kills[key] = n + 1;
        }
    }
}
