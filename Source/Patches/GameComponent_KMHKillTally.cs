using System.Collections.Generic;
using RimWorld;
using Verse;

namespace KMHPatch.Patches
{
    public class GameComponent_KMHKillTally : GameComponent
    {
        private Dictionary<string, int> _kills = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        private int _colonistDeaths;

        // Colonists who died here, so the colony's kill total does not fall when its killers do.
        private List<FallenColonist> _fallen = new List<FallenColonist>();
        private KillSplit _folded;
        internal const int MaxFallen = 500;

        private List<CombatEvent> _combat = new List<CombatEvent>();
        internal const int MaxCombatEvents = 40;

        public GameComponent_KMHKillTally(Game game) { }

        public static GameComponent_KMHKillTally Current => Verse.Current.Game?.GetComponent<GameComponent_KMHKillTally>();

        public int KillsOf(string defName)
            => !string.IsNullOrEmpty(defName) && _kills.TryGetValue(defName, out int n) ? n : 0;

        public int ColonistDeaths => _colonistDeaths;

        public void Bump(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _kills.TryGetValue(key, out int n);
            _kills[key] = n + 1;
        }

        internal struct KillSplit
        {
            public long Kills, Humanlike, Mechanoid, Animal;
            public void Add(KillSplit o) { Kills += o.Kills; Humanlike += o.Humanlike; Mechanoid += o.Mechanoid; Animal += o.Animal; }
        }

        internal sealed class FallenColonist : IExposable
        {
            public string Id = "", Name = "";
            public int Kills, Humanlike, Mechanoid, Animal, DiedTick;

            internal KillSplit Split => new KillSplit { Kills = Kills, Humanlike = Humanlike, Mechanoid = Mechanoid, Animal = Animal };

            public void ExposeData()
            {
                Scribe_Values.Look(ref Id, "id", "");
                Scribe_Values.Look(ref Name, "name", "");
                Scribe_Values.Look(ref Kills, "kills", 0);
                Scribe_Values.Look(ref Humanlike, "humanlike", 0);
                Scribe_Values.Look(ref Mechanoid, "mechanoid", 0);
                Scribe_Values.Look(ref Animal, "animal", 0);
                Scribe_Values.Look(ref DiedTick, "diedTick", 0);
            }
        }

        internal sealed class CombatEvent : IExposable
        {
            public string ActorId = "", Text = "";
            public int Tick;

            public void ExposeData()
            {
                Scribe_Values.Look(ref ActorId, "actor", "");
                Scribe_Values.Look(ref Text, "text", "");
                Scribe_Values.Look(ref Tick, "tick", 0);
            }
        }

        internal static FallenColonist FallenFrom(Pawn p, int tick) => new FallenColonist
        {
            Id = p.ThingID,
            Name = p.Name?.ToStringShort ?? p.LabelShortCap,
            Kills = Record(p, RecordDefOf.Kills),
            Humanlike = Record(p, RecordDefOf.KillsHumanlikes),
            Mechanoid = Record(p, RecordDefOf.KillsMechanoids),
            Animal = Record(p, RecordDefOf.KillsAnimals),
            DiedTick = tick,
        };

        private static int Record(Pawn p, RecordDef def)
        {
            try { return def != null && p?.records != null ? (int)p.records.GetValue(def) : 0; }
            catch { return 0; }
        }

        // A second death of one pawn (resurrected, then killed) replaces its row: it is one colonist's kills, counted once.
        internal void RecordFallen(FallenColonist f)
        {
            if (f == null || string.IsNullOrEmpty(f.Id)) return;
            _colonistDeaths++;
            _fallen.RemoveAll(x => x == null || x.Id == f.Id);
            _fallen.Add(f);
            while (_fallen.Count > MaxFallen)
            {
                _folded.Add(_fallen[0].Split);
                _fallen.RemoveAt(0);
            }
        }

        internal int FallenCount => _fallen.Count;

        // What fallen colonists killed, leaving out any now alive again: their kills are already in the living count.
        internal KillSplit FallenKills(ICollection<string> livingIds)
        {
            KillSplit sum = _folded;
            foreach (FallenColonist f in _fallen)
                if (f != null && (livingIds == null || !livingIds.Contains(f.Id))) sum.Add(f.Split);
            return sum;
        }

        internal void NoteCombat(string actorId, string text, int tick)
        {
            if (string.IsNullOrEmpty(actorId) || string.IsNullOrEmpty(text)) return;
            _combat.Add(new CombatEvent { ActorId = actorId, Text = KMHPatch.UI.KmhDisplayText.Inert(text, 110), Tick = tick });
            if (_combat.Count > MaxCombatEvents) _combat.RemoveRange(0, _combat.Count - MaxCombatEvents);
        }

        internal int CombatCount => _combat.Count;

        internal List<string> RecentCombatOf(string actorId, int max)
        {
            var outList = new List<string>();
            for (int i = _combat.Count - 1; i >= 0 && outList.Count < max; i--)
                if (_combat[i] != null && _combat[i].ActorId == actorId) outList.Add(_combat[i].Text);
            return outList;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _kills, "kmhKills", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref _colonistDeaths, "kmhColonistDeaths", 0);
            Scribe_Collections.Look(ref _fallen, "kmhFallen", LookMode.Deep);
            Scribe_Values.Look(ref _folded.Kills, "kmhFoldedKills", 0L);
            Scribe_Values.Look(ref _folded.Humanlike, "kmhFoldedHumanlike", 0L);
            Scribe_Values.Look(ref _folded.Mechanoid, "kmhFoldedMechanoid", 0L);
            Scribe_Values.Look(ref _folded.Animal, "kmhFoldedAnimal", 0L);
            Scribe_Collections.Look(ref _combat, "kmhCombat", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Scribe drops the case-insensitive comparer on load.
                _kills = new Dictionary<string, int>(_kills ?? new Dictionary<string, int>(),
                                                     System.StringComparer.OrdinalIgnoreCase);
                if (_fallen == null) _fallen = new List<FallenColonist>();
                if (_combat == null) _combat = new List<CombatEvent>();
            }
        }
    }
}
