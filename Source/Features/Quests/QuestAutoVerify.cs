using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Quests.Dto;
using KMHPatch.Patches;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Quests
{
    // Auto-reports observable claimed quests, while Report stays the fallback for quests we can't verify.
    public class QuestAutoVerify : GameComponent
    {
        private const int IntervalTicks = 250; // ~4s at 1x
        private int _next;

        private const int RetryTicks = 1800; // ~30s: re-report while the quest stays Claimed (server may gate early verifies)

        private HashSet<long>        _sent         = new HashSet<long>();
        private Dictionary<long, int> _huntBaseline = new Dictionary<long, int>();
        private Dictionary<long, int> _sentAtTick   = new Dictionary<long, int>();

        public QuestAutoVerify(Game game) { }

        // Persist quest progress so reloads don't replay completions or reset hunt baselines.
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _sent, "kmhQavSent", LookMode.Value);
            Scribe_Collections.Look(ref _huntBaseline, "kmhQavHuntBaseline", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref _sentAtTick, "kmhQavSentTick", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _sent ??= new HashSet<long>();
                _huntBaseline ??= new Dictionary<long, int>();
                _sentAtTick ??= new Dictionary<long, int>();
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
            string me = KmhSession.Me;
            if (string.IsNullOrEmpty(me)) return;

            foreach (QuestEntry q in snap.Quests)
            {
                if (q == null) continue;
                if (q.State != QuestEntry.StateClaimed) continue;   // acceptance changes the state, ending the loop
                if (!KmhSession.Same(q.ClaimedByUsername, me)) continue;

                bool done = q.Kind == QuestEntry.KindHunt  ? HuntDone(q)
                          : q.Kind == QuestEntry.KindBuild ? BuildDone(q)
                          : false;

                if (done)
                {
                    // Retried while Claimed: the server's anti-macro floor rejects a legitimately fast completion once.
                    int nowTick = Find.TickManager.TicksGame;
                    bool first = !_sent.Contains(q.Id);
                    if (!first && _sentAtTick.TryGetValue(q.Id, out int last) && nowTick - last < RetryTicks) continue;
                    QuestHandler.TryVerify(q.Id);
                    _sent.Add(q.Id);
                    _sentAtTick[q.Id] = nowTick;
                    // Confirms the player-quest auto-verify pipeline fired (the manual Report button is the fallback).
                    if (first) KmhLog.Info($"KMH: auto-verified claimed quest #{q.Id} ({q.Kind}).");
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
                // own colonies only - a visited/hosted player's map renders their structures as Faction.OfPlayer, which would falsely satisfy a build quest
                if (map?.listerThings == null || !map.IsPlayerHome) continue;
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
