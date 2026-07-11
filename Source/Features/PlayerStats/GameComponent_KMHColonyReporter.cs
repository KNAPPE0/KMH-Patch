using System;
using System.Collections.Generic;
using System.Linq;
using KMHPatch.Diagnostics;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.SubProtocol;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.PlayerStats
{
    // Sends colony + top colonist summary to the leaderboard on a real-time timer, with playtime tracked locally.
    public class GameComponent_KMHColonyReporter : GameComponent
    {
        private const float ReportIntervalSec = 120f;   // ~2 min between uploads
        private const int   RecentCombatMax    = 8;       // cap the combat log we ship

        private static double _playSeconds;   // cumulative real seconds in-game (persisted)
        private float  _reportTimer = 6f;     // first upload ~6s after load

        // Per-save id: fresh for a new colony, restored on load. The server uses a change here to detect a save reset
        // (anti-exploit for treasury farming), so it must NOT be static - each game gets its own.
        private string _saveId;
        public string SaveId
        {
            get
            {
                if (string.IsNullOrEmpty(_saveId)) _saveId = Guid.NewGuid().ToString("N");
                return _saveId;
            }
        }

        public GameComponent_KMHColonyReporter(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _playSeconds, "kmhPlaySeconds", 0.0);
            Scribe_Values.Look(ref _saveId, "kmhSaveId", null);
        }

        public override void GameComponentUpdate()
        {
            // Real wall-clock time the game has been open (unaffected by RimWorld's tick speed / pause).
            _playSeconds += Time.deltaTime;

            if (!KmhDispatcher.IsKmhServer) return;
            _reportTimer -= Time.deltaTime;
            if (_reportTimer > 0f) return;
            _reportTimer = ReportIntervalSec;
            SendNow();
        }

        // Builds and uploads a colony report now; safe from UI and rate-limited server-side.
        private static float _lastSendRealtime = -999f;

        public static void SendNow()
        {
            if (!KmhDispatcher.IsKmhServer) return;
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null) return;
            if (Time.realtimeSinceStartup - _lastSendRealtime < 20f) return;
            try
            {
                ColonyReport report = BuildReport();
                if (report != null && PlayerStatsHandler.SendColonyReport(report))
                {
                    _lastSendRealtime = Time.realtimeSinceStartup;
                    // periodic; debug-only so it doesn't flood the log
                    KmhLog.Debug($"KMH: sent colony report (wealth {report.Wealth}, kills {report.Kills}, {report.Roster?.Count ?? 0} colonist(s))");
                }
            }
            catch (Exception ex) { KmhLog.Warn($"Colony reporter threw: {ex.Message}"); }
        }

        private static ColonyReport BuildReport()
        {
            List<Pawn> colonists = FreeColonists();
            Pawn colonist = PickTopColonist(colonists);

            long wealth = 0;
            List<Map> maps = Find.Maps;
            if (maps != null)
                foreach (Map m in maps)
                    if (m != null && m.IsPlayerHome && m.wealthWatcher != null)
                        wealth += (long)m.wealthWatcher.WealthTotal;

            long kills = 0, kHuman = 0, kMech = 0, kAnimal = 0;
            foreach (Pawn p in colonists)
            {
                kills   += KillCount(p);
                kHuman  += RecordInt(p, RecordDefOf.KillsHumanlikes);
                kMech   += RecordInt(p, RecordDefOf.KillsMechanoids);
                kAnimal += RecordInt(p, RecordDefOf.KillsAnimals);
            }

            (int buildings, int turrets, int traps) = StructureCounts();

            ColonyReport r = new ColonyReport
            {
                SaveId          = Current.Game?.GetComponent<GameComponent_KMHColonyReporter>()?.SaveId ?? "",
                ColonyName      = Faction.OfPlayer?.Name ?? "",
                ColonyAgeDays   = Find.TickManager != null ? Find.TickManager.TicksGame / 60000 : 0,
                TimePlayedHours = (int)(_playSeconds / 3600.0),
                Wealth          = wealth,
                Kills           = kills,
                Population       = colonists.Count,
                KillsHumanlike   = kHuman,
                KillsMechanoid   = kMech,
                KillsAnimal      = kAnimal,
                RaidsSurvived    = RaidsSurvived(),
                PawnsLost        = Patches.GameComponent_KMHKillTally.Current?.ColonistDeaths ?? 0,
                DevelopmentScore = FinishedResearch() * 10 + buildings,
                DefenseScore     = turrets * 5 + traps * 2,
            };

            if (colonist != null)
            {
                r.TopColonistName  = colonist.Name?.ToStringShort ?? colonist.LabelShortCap;
                r.TopColonistTitle = TopColonistTitle(colonist);
                r.TopColonistKills = KillCount(colonist);
                r.Colonist      = BuildColonistProfile(colonist);
            }
            r.Roster = BuildRoster(colonists);
            return r;
        }

        private static List<Pawn> FreeColonists()
        {
            List<Pawn> outList = new List<Pawn>();
            List<Map> maps = Find.Maps;
            if (maps == null) return outList;
            foreach (Map m in maps)
            {
                // own colonies only - a visited/hosted player's map isn't IsPlayerHome, so its pawns must not be reported as ours (same guard wealth uses)
                if (m == null || !m.IsPlayerHome || m.mapPawns?.FreeColonists == null) continue;
                foreach (Pawn p in m.mapPawns.FreeColonists)
                    if (p != null && !p.Dead && p.HostFaction == null) outList.Add(p);
            }
            return outList;
        }

        // Compact per-colonist roster (≤10) for the per-skill Colonist Records boards. Owner + colony are stamped server-side.
        private static List<ColonistEntry> BuildRoster(List<Pawn> colonists)
        {
            List<ColonistEntry> outList = new List<ColonistEntry>();
            int n = Math.Min(colonists.Count, 10);
            for (int i = 0; i < n; i++)
            {
                Pawn p = colonists[i];
                if (p == null) continue;
                outList.Add(new ColonistEntry
                {
                    Name  = p.Name?.ToStringShort ?? p.LabelShortCap,
                    Title = TopColonistTitle(p),
                    Age   = p.ageTracker?.AgeBiologicalYears ?? 0,
                    Days  = DaysInColony(p),
                    Kills = KillCount(p),
                    SkShooting     = SkillLevel(p, SkillDefOf.Shooting),
                    SkMelee        = SkillLevel(p, SkillDefOf.Melee),
                    SkMedicine     = SkillLevel(p, SkillDefOf.Medicine),
                    SkCrafting     = SkillLevel(p, SkillDefOf.Crafting),
                    SkConstruction = SkillLevel(p, SkillDefOf.Construction),
                });
            }
            return outList;
        }

        private static int SkillLevel(Pawn p, SkillDef def)
        {
            try { return p?.skills?.GetSkill(def)?.Level ?? 0; }
            catch { return 0; }
        }

        // Enemy raids this colony has faced (≈ survived, since the colony is still alive to report).
        private static int RaidsSurvived()
        {
            try { return Find.StoryWatcher?.statsRecord?.numRaidsEnemy ?? 0; }
            catch { return 0; }
        }

        // Completed research projects - a rough "how developed" signal.
        private static int FinishedResearch()
        {
            try
            {
                int n = 0;
                foreach (ResearchProjectDef r in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                    if (r != null && r.IsFinished) n++;
                return n;
            }
            catch { return 0; }
        }

        // One pass over player buildings across home maps: total buildings + turrets + traps (for dev/defense scores).
        private static (int buildings, int turrets, int traps) StructureCounts()
        {
            int b = 0, tr = 0, tp = 0;
            try
            {
                List<Map> maps = Find.Maps;
                if (maps == null) return (0, 0, 0);
                foreach (Map m in maps)
                {
                    if (m?.IsPlayerHome != true || m.listerBuildings?.allBuildingsColonist == null) continue;
                    foreach (Building bd in m.listerBuildings.allBuildingsColonist)
                    {
                        if (bd == null) continue;
                        b++;
                        if (bd is Building_Turret) tr++;
                        else if (bd is Building_Trap) tp++;
                    }
                }
            }
            catch { }
            return (b, tr, tp);
        }

        // Deadliest colonist: most kills, tiebreak by combat skill (Shooting + Melee).
        private static Pawn PickTopColonist(List<Pawn> colonists)
        {
            Pawn best = null; int bestKills = -1, bestCombat = -1;
            foreach (Pawn p in colonists)
            {
                int k = KillCount(p);
                int c = CombatSkill(p);
                if (k > bestKills || (k == bestKills && c > bestCombat))
                {
                    best = p; bestKills = k; bestCombat = c;
                }
            }
            return best;
        }

        private static int KillCount(Pawn p)
        {
            try { return p?.records != null ? (int)p.records.GetValue(RecordDefOf.Kills) : 0; }
            catch { return 0; }
        }

        private static int CombatSkill(Pawn p)
        {
            try
            {
                int s = p?.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                int m = p?.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                return s + m;
            }
            catch { return 0; }
        }

        private static string TopColonistTitle(Pawn p)
        {
            try { return p.story?.Adulthood?.title ?? p.story?.Childhood?.title ?? "Colonist"; }
            catch { return "Colonist"; }
        }

        private static ColonistProfile BuildColonistProfile(Pawn p)
        {
            ColonistProfile d = new ColonistProfile();
            try
            {
                d.Name         = p.Name?.ToStringFull ?? p.LabelShortCap;
                d.Title        = TopColonistTitle(p);
                d.GenderAge    = $"{p.gender}, age {p.ageTracker?.AgeBiologicalYears ?? 0} ({p.ageTracker?.AgeChronologicalYears ?? 0})";
                d.Descriptor   = Descriptor(p);
                d.DaysInColony = DaysInColony(p);

                d.Childhood = p.story?.Childhood?.title ?? "";
                d.Adulthood = p.story?.Adulthood?.title ?? "";

                if (p.story?.traits?.allTraits != null)
                    foreach (Trait t in p.story.traits.allTraits) if (t != null) d.Traits.Add(t.LabelCap);

                if (p.skills?.skills != null)
                    foreach (SkillRecord sk in p.skills.skills)
                        if (sk?.def != null)
                            d.Skills.Add(new ColonistSkill { Name = sk.def.skillLabel.CapitalizeFirst(), Level = sk.Level, Passion = (int)sk.passion });

                d.Incapable = IncapableOf(p);

                // health
                d.HealthPct = Pct(p.health?.summaryHealth?.SummaryHealthPercent ?? 0f);
                d.PainPct   = Pct(p.health?.hediffSet?.PainTotal ?? 0f);
                d.Capacities = Capacities(p);
                d.Conditions = Conditions(p);

                // combat
                d.TotalKills     = RecordInt(p, RecordDefOf.Kills);
                d.HumanlikeKills = RecordInt(p, RecordDefOf.KillsHumanlikes);
                d.MechanoidKills = RecordInt(p, RecordDefOf.KillsMechanoids);
                d.AnimalKills    = RecordInt(p, RecordDefOf.KillsAnimals);
                d.DamageTaken    = RecordInt(p, RecordDefOf.DamageTaken);
                ThingWithComps wep = p.equipment?.Primary;
                d.Weapon = wep?.LabelCap ?? "None";
                d.WeaponQuality = (wep != null && wep.TryGetQuality(out QualityCategory q)) ? q.GetLabel().CapitalizeFirst() : "None";
            }
            catch (Exception ex) { KmhLog.Warn($"Colonist detail partial: {ex.Message}"); }
            return d;
        }

        private static string Descriptor(Pawn p)
        {
            try
            {
                string xeno = p.genes?.XenotypeLabelCap;
                return string.IsNullOrEmpty(xeno) ? "Colony" : $"{xeno} • Colony";
            }
            catch { return "Colony"; }
        }

        private static int DaysInColony(Pawn p)
        {
            try { return (int)(p.records.GetValue(RecordDefOf.TimeAsColonistOrColonyAnimal) / 60000f); }
            catch { return 0; }
        }

        private static List<string> IncapableOf(Pawn p)
        {
            List<string> outList = new List<string>();
            try
            {
                WorkTags disabled = p.CombinedDisabledWorkTags;
                foreach (WorkTags tag in Enum.GetValues(typeof(WorkTags)))
                    if (tag != WorkTags.None && (disabled & tag) != 0) outList.Add(tag.LabelTranslated().CapitalizeFirst());
            }
            catch { }
            return outList;
        }

        // Resolved by defName (not DefOf) so version-specific capacities like Eating still resolve and any missing one just drops out.
        private static readonly string[] CapacityNames =
        {
            "Consciousness", "Moving", "Manipulation", "Talking", "Eating", "Sight",
            "Hearing", "Breathing", "BloodFiltration", "BloodPumping",
        };

        private static List<ColonistCapacity> Capacities(Pawn p)
        {
            List<ColonistCapacity> outList = new List<ColonistCapacity>();
            try
            {
                if (p.health?.capacities == null) return outList;
                foreach (string name in CapacityNames)
                {
                    PawnCapacityDef cap = DefDatabase<PawnCapacityDef>.GetNamedSilentFail(name);
                    if (cap == null) continue;
                    if (!PawnCapacityUtility.BodyCanEverDoCapacity(p.RaceProps.body, cap)) continue;
                    outList.Add(new ColonistCapacity { Name = cap.LabelCap, Pct = Pct(p.health.capacities.GetLevel(cap)) });
                }
            }
            catch { }
            return outList;
        }

        private static List<string> Conditions(Pawn p)
        {
            List<string> outList = new List<string>();
            try
            {
                if (p.health?.hediffSet?.hediffs == null) return outList;
                foreach (Hediff h in p.health.hediffSet.hediffs)
                {
                    if (h == null || !h.Visible) continue;
                    if (!(h.def?.isBad ?? false)) continue;
                    outList.Add(h.LabelCap);
                    if (outList.Count >= RecentCombatMax) break;
                }
            }
            catch { }
            return outList;
        }

        private static int RecordInt(Pawn p, RecordDef def)
        {
            try { return def != null && p?.records != null ? (int)p.records.GetValue(def) : 0; }
            catch { return 0; }
        }

        private static int Pct(float v) => Mathf.Clamp((int)Math.Round(v * 100f), 0, 100);
    }
}
