using System;
using System.Collections.Generic;
using GameClient.Misc;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Quests.Dto;
using KMHPatch.Patches;
using KMHPatch.SubProtocol;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Quests
{
    // Auto-reports observable claimed quests, while Report stays the fallback for quests we can't verify.
    public class QuestAutoVerify : GameComponent
    {
        private const int IntervalTicks = 250; // ~4s at 1x
        private int _next;

        private HashSet<long>        _sent         = new HashSet<long>();
        private Dictionary<long, int> _huntBaseline = new Dictionary<long, int>();

        public QuestAutoVerify(Game game) { }

        // Persist quest progress so reloads don't replay completions or reset hunt baselines.
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _sent, "kmhQavSent", LookMode.Value);
            Scribe_Collections.Look(ref _huntBaseline, "kmhQavHuntBaseline", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _sent ??= new HashSet<long>();
                _huntBaseline ??= new Dictionary<long, int>();
            }
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame < _next) return;
            _next = Find.TickManager.TicksGame + IntervalTicks;
            try { Scan(); }
            catch (Exception ex) { KmhLog.Warn($"Quest auto-verify threw: {ex.Message}"); }
        }

        private void Scan()
        {
            if (!KmhDispatcher.IsKmhServer || !QuestCache.HasSnapshot) return;
            QuestSnapshot snap = QuestCache.Snapshot;
            if (snap?.Quests == null) return;
            string me = SessionHandler.Username;
            if (string.IsNullOrEmpty(me)) return;

            foreach (QuestEntry q in snap.Quests)
            {
                if (q == null || _sent.Contains(q.Id)) continue;
                if (q.State != QuestEntry.StateClaimed) continue;
                if (!string.Equals(q.ClaimedByUsername, me, StringComparison.OrdinalIgnoreCase)) continue;

                bool done = q.Kind == QuestEntry.KindHunt  ? HuntDone(q)
                          : q.Kind == QuestEntry.KindBuild ? BuildDone(q)
                          : false;

                if (done)
                {
                    QuestHandler.TryVerify(q.Id);
                    _sent.Add(q.Id);
                    // Confirms the player-quest auto-verify pipeline fired (the manual Report button is the fallback).
                    KmhLog.Info($"KMH: auto-verified claimed quest #{q.Id} ({q.Kind}).");
                }
            }
        }

        private bool HuntDone(QuestEntry q)
        {
            if (string.IsNullOrEmpty(q.HuntTargetDefName) || q.HuntTargetCount <= 0) return false;
            int now = Patch_Pawn_Kill_HuntTally.KillsOf(q.HuntTargetDefName);
            if (!_huntBaseline.TryGetValue(q.Id, out int baseline)) { _huntBaseline[q.Id] = now; baseline = now; }
            return now - baseline >= q.HuntTargetCount;
        }

        private static bool BuildDone(QuestEntry q)
        {
            if (string.IsNullOrEmpty(q.BuildStructureDefName) || q.BuildCount <= 0) return false;
            // Exact match, then a case-insensitive fallback so a "sandbags" vs "Sandbags" typo still tracks.
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(q.BuildStructureDefName)
                        ?? DefDatabase<ThingDef>.AllDefsListForReading.Find(d => string.Equals(d.defName, q.BuildStructureDefName, StringComparison.OrdinalIgnoreCase));
            if (def == null) return false;

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
                if (count >= q.BuildCount) return true;
            }
            return count >= q.BuildCount;
        }
    }
}
