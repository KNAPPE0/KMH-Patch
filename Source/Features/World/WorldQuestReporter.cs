using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.World.Dto;
using KMHPatch.Patches;
using KMHPatch.SubProtocol;
using RimWorld;
using Verse;

namespace KMHPatch.Features.World
{
    // Auto-reports global quest progress; cumulative and safe to resend since the server keeps each player's max.
    public class WorldQuestReporter : GameComponent
    {
        private const int IntervalTicks = 250; // ~4s at 1x

        private int _next;
        private Dictionary<long, int> _huntBaseline  = new Dictionary<long, int>(); // kills-at-first-sight per quest
        private Dictionary<long, int> _buildBaseline = new Dictionary<long, int>(); // standing count at first sight per quest
        private Dictionary<long, int> _lastSent      = new Dictionary<long, int>(); // last value we reported per quest

        public WorldQuestReporter(Game game) { }

        // Persisted so a reload resumes each quest's contribution (alongside the persistent kill tally).
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _huntBaseline, "kmhWqHuntBaseline", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref _buildBaseline, "kmhWqBuildBaseline", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref _lastSent, "kmhWqLastSent", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _huntBaseline ??= new Dictionary<long, int>();
                _buildBaseline ??= new Dictionary<long, int>();
                _lastSent ??= new Dictionary<long, int>();
            }
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame < _next) return;
            _next = Find.TickManager.TicksGame + IntervalTicks;
            try { Scan(); }
            catch (Exception ex) { KmhLog.Warn($"World quest reporter threw: {ex.Message}"); }
        }

        private void Scan()
        {
            if (!KmhDispatcher.IsKmhServer || !WorldCache.HasSnapshot) return;
            WorldSnapshot snap = WorldCache.Snapshot;
            if (snap?.ServerQuests == null || snap.ServerQuests.Count == 0) return;

            HashSet<long> seen = new HashSet<long>();
            foreach (ServerQuestDto q in snap.ServerQuests)
            {
                if (q == null) continue;
                if (!string.Equals(q.State, ServerQuestDto.StateActive, StringComparison.OrdinalIgnoreCase)) continue;
                seen.Add(q.Id);

                int total;
                if (string.Equals(q.Objective, ServerQuestDto.ObjHunt, StringComparison.OrdinalIgnoreCase))
                    total = HuntContribution(q);
                else if (string.Equals(q.Objective, ServerQuestDto.ObjBuild, StringComparison.OrdinalIgnoreCase))
                    total = BuildContribution(q);
                else continue; // deliver isn't auto-reported yet

                if (total <= 0) continue;                                          // nothing to contribute
                if (_lastSent.TryGetValue(q.Id, out int prev) && total <= prev) continue; // only resend on growth
                if (WorldHandler.SendContribution(q.Id, total))
                {
                    _lastSent[q.Id] = total;
                    KmhLog.Debug($"KMH: reported {total} toward global quest #{q.Id} ({q.Objective} {q.TargetDefName})");
                }
            }

            // Forget quests that are gone so the maps don't grow unbounded across a long session.
            Prune(_huntBaseline, seen);
            Prune(_buildBaseline, seen);
            Prune(_lastSent, seen);
        }

        // Kills of the target since this quest first appeared (baseline = tally at first sight).
        private int HuntContribution(ServerQuestDto q)
        {
            if (string.IsNullOrEmpty(q.TargetDefName)) return -1;
            int now = Patch_Pawn_Kill_HuntTally.KillsOf(q.TargetDefName);
            if (!_huntBaseline.TryGetValue(q.Id, out int baseline)) { _huntBaseline[q.Id] = now; baseline = now; }
            int delta = now - baseline;
            return delta < 0 ? 0 : delta;
        }

        // Structures of the target built since this quest first appeared (baseline = standing count at first sight),
        // so things the player already had placed never count - only new builds after the quest went live do.
        private int BuildContribution(ServerQuestDto q)
        {
            if (string.IsNullOrEmpty(q.TargetDefName)) return -1;
            // Exact match first; fall back to a case-insensitive scan so a "sandbags" vs "Sandbags" typo in the
            // owner's quest command still tracks (the server has no def DB to validate the name at creation).
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(q.TargetDefName)
                        ?? DefDatabase<ThingDef>.AllDefsListForReading.Find(d => string.Equals(d.defName, q.TargetDefName, StringComparison.OrdinalIgnoreCase));
            if (def == null) return -1;

            int now = CountStanding(def);
            if (!_buildBaseline.TryGetValue(q.Id, out int baseline)) { _buildBaseline[q.Id] = now; baseline = now; }
            int delta = now - baseline;
            return delta < 0 ? 0 : delta;
        }

        // Player-faction structures of the def currently standing across all our maps.
        private static int CountStanding(ThingDef def)
        {
            int count = 0;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map?.listerThings == null) continue;
                List<Thing> things = map.listerThings.ThingsOfDef(def);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing t = things[j];
                    if (t != null && t.Spawned && t.Faction == Faction.OfPlayer) count++;
                }
            }
            return count;
        }

        private static void Prune(Dictionary<long, int> map, HashSet<long> keep)
        {
            if (map.Count == 0) return;
            List<long> drop = null;
            foreach (long id in map.Keys)
                if (!keep.Contains(id)) (drop ?? (drop = new List<long>())).Add(id);
            if (drop != null) foreach (long id in drop) map.Remove(id);
        }
    }
}
