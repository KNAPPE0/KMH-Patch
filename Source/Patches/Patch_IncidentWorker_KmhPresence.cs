using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    // Records only whether a raid came by drop pod; the raid itself is read from the map, never from the incident.
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    internal static class Patch_IncidentWorker_KmhPresence
    {
        [HarmonyPostfix]
        private static void Postfix(IncidentParms parms, bool __result)
        {
            try
            {
                if (!__result || !(parms?.target is Map map)) return;
                Type arrival = parms.raidArrivalMode?.workerClass;
                if (arrival == null || arrival.Name.IndexOf("Drop", StringComparison.Ordinal) < 0) return;
                Features.Discord.KmhPresenceSignals.NoteDropRaid(map, Find.TickManager.TicksGame);
            }
            catch { }
        }
    }
}
