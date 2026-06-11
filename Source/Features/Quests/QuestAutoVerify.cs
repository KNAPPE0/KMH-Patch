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
    // Auto-reports verifiable claimed quests so the player needn't click Report. Slow-tick poll: hunt = matching
    // non-colonist kills since claim >= target, build = target count standing on a player map. Report stays the
    // manual fallback for kinds we can't observe (escort, defend), category-only hunt targets, or missed detection
    //
    // Auto-instantiated by RimWorld for every GameComponent subclass.
    public class QuestAutoVerify : GameComponent
    {
        private const int IntervalTicks = 250; // ~4s at 1x
        private int _next;

        private readonly HashSet<long>        _sent         = new HashSet<long>();
        private readonly Dictionary<long, int> _huntBaseline = new Dictionary<long, int>();

        public QuestAutoVerify(Game game) { }

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
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(q.BuildStructureDefName);
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
