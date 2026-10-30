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
    public class GameComponent_KMHColonyReporter : GameComponent
    {
        private const float ReportIntervalSec = 120f;
        private const int   RecentCombatMax    = 8;
        internal const int  RosterMax          = 10;

        private float  _reportTimer = 6f;

        // The server reads a change here as a save reset, so this must NOT be static - each game gets its own.
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
            Scribe_Values.Look(ref _saveId, "kmhSaveId", null);
        }

        public override void GameComponentUpdate()
        {
            if (!KmhDispatcher.IsKmhServer) return;
            _reportTimer -= Time.deltaTime;
            if (_reportTimer > 0f) return;
            _reportTimer = ReportIntervalSec;
            Send();
        }

        private static float _lastSendRealtime = -999f;
        private static int   _sentInSession = -1;

        // For windows: only a connection with no report yet gets one; after that the timer keeps it current.
        public static void SendNow()
        {
            if (!UiMaySend(_sentInSession, KmhDispatcher.SessionGeneration)) return;
            Send();
        }

        // Once per connection: Standings refreshes every 12 s and the server drops reports inside its 30 s limit.
        internal static bool UiMaySend(int sentInSession, int sessionNow) => sentInSession != sessionNow;

        private static void Send()
        {
            if (!KmhDispatcher.IsKmhServer) return;
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null) return;
            if (_sentInSession == KmhDispatcher.SessionGeneration && Time.realtimeSinceStartup - _lastSendRealtime < 20f) return;
            try
            {
                ColonyReport report = BuildReport();
                if (report != null && PlayerStatsHandler.SendColonyReport(report))
                {
                    _lastSendRealtime = Time.realtimeSinceStartup;
                    _sentInSession = KmhDispatcher.SessionGeneration;
                    KmhLog.Debug($"KMH: sent colony report (wealth {report.Wealth}, kills {report.Kills}, {report.Roster?.Count ?? 0} colonist(s))");
                }
            }
            catch (Exception ex) { KmhLog.Warn($"Colony reporter threw: {ex.Message}"); }
        }

        private static ColonyReport BuildReport()
        {
            List<Pawn> colonists = LivingColonists();
            Pawn colonist = PickTopColonist(colonists);

            List<SettlementReport> settlements = Settlements(colonists);
            long wealth = 0;
            foreach (SettlementReport s in settlements) wealth += s.Wealth;

            long kills = 0, kHuman = 0, kMech = 0, kAnimal = 0;
            var livingIds = new HashSet<string>();
            foreach (Pawn p in colonists)
            {
                kills   += KillCount(p);
                kHuman  += RecordInt(p, RecordDefOf.KillsHumanlikes);
                kMech   += RecordInt(p, RecordDefOf.KillsMechanoids);
                kAnimal += RecordInt(p, RecordDefOf.KillsAnimals);
                livingIds.Add(p.ThingID);
            }
            Patches.GameComponent_KMHKillTally tally = Patches.GameComponent_KMHKillTally.Current;
            if (tally != null)
            {
                Patches.GameComponent_KMHKillTally.KillSplit fallen = tally.FallenKills(livingIds);
                kills += fallen.Kills; kHuman += fallen.Humanlike; kMech += fallen.Mechanoid; kAnimal += fallen.Animal;
            }

            (int buildings, int turrets, int traps) = StructureCounts();

            ColonyReport r = new ColonyReport
            {
                SaveId          = Current.Game?.GetComponent<GameComponent_KMHColonyReporter>()?.SaveId ?? "",
                ColonyName      = Faction.OfPlayer?.Name ?? "",
                ColonyAgeDays   = Find.TickManager != null ? Find.TickManager.TicksGame / 60000 : 0,
                TimePlayedHours = PlayedHours(),
                Wealth          = wealth,
                Kills           = kills,
                Population       = colonists.Count,
                KillsHumanlike   = kHuman,
                KillsMechanoid   = kMech,
                KillsAnimal      = kAnimal,
                RaidsSurvived    = RaidsSurvived(),
                PawnsLost        = tally?.ColonistDeaths ?? 0,
                DevelopmentScore = FinishedResearch() * 10 + buildings,
                DefenseScore     = turrets * 5 + traps * 2,
            };

            if (colonist != null)
            {
                r.TopColonistName  = colonist.Name?.ToStringShort ?? colonist.LabelShortCap;
                r.TopColonistTitle = TopColonistTitle(colonist);
                r.TopColonistKills = KillCount(colonist);
                r.Colonist      = BuildColonistProfile(colonist, tally);
            }
            r.Roster = BuildRoster(colonists);
            r.Settlements = settlements;
            return r;
        }

        // RimWorld's per-game clock, which stops while nobody touches the game; a static would span games.
        private static int PlayedHours()
        {
            try { return (int)((Find.GameInfo?.RealPlayTimeInteracting ?? 0f) / 3600f); }
            catch { return 0; }
        }

        // RimWorld's IsColonist, as its own records count (slaves yes, ghouls no), for population, deaths and kills alike.
        public static bool IsCountedColonist(Pawn p) => p != null && !p.Dead && p.IsColonist;

        // RWT puts other players' pawns in its own factions, so they never count here.
        internal static List<Pawn> LivingColonists()
        {
            var outList = new List<Pawn>();
            var seen = new HashSet<Pawn>();
            foreach (Pawn p in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists)
                if (IsCountedColonist(p) && seen.Add(p)) outList.Add(p);
            // 1.7 already lists the gravship's crew; 1.6 may not, and the set drops the overlap.
            try
            {
                RimWorld.Planet.Gravship ship = Find.CurrentGravship;
                if (ship != null)
                    foreach (Pawn p in ship.Pawns)
                        if (IsCountedColonist(p) && seen.Add(p)) outList.Add(p);
            }
            catch (Exception ex) { KmhLog.Debug($"Colony reporter: gravship crew unreadable: {ex.Message}"); }
            var held = new List<Pawn>();
            Sites.WorldComponent_KMHSiteWorkers.Instance?.AppendHeldPawns(held);
            foreach (Pawn p in held)
                if (IsCountedColonist(p) && seen.Add(p)) outList.Add(p);
            return outList;
        }

        // One row per home map counting the colonists on it; those away (caravans, sites, flight) are in the total only.
        private static List<SettlementReport> Settlements(List<Pawn> colonists)
        {
            List<SettlementReport> outList = new List<SettlementReport>();
            List<Map> maps = Find.Maps;
            if (maps == null) return outList;
            var pop = new Dictionary<Map, int>();
            foreach (Pawn p in colonists)
            {
                Map at = p.MapHeld;
                if (at != null) pop[at] = (pop.TryGetValue(at, out int n) ? n : 0) + 1;
            }
            foreach (Map m in maps)
            {
                // A visited player's map is not IsPlayerHome, so its wealth is never reported as ours.
                if (m == null || !m.IsPlayerHome) continue;
                outList.Add(new SettlementReport
                {
                    Name       = SettlementName(m),
                    Wealth     = m.wealthWatcher != null ? (long)m.wealthWatcher.WealthTotal : 0,
                    Population = pop.TryGetValue(m, out int here) ? here : 0,
                });
            }
            return outList;
        }

        private static string SettlementName(Map m)
        {
            try
            {
                string n = m.Parent?.LabelCap;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            return "Settlement";
        }

        // Owner and colony are stamped server-side, never taken from this report.
        private static List<ColonistEntry> BuildRoster(List<Pawn> colonists)
        {
            List<ColonistEntry> outList = new List<ColonistEntry>();
            foreach (Pawn p in colonists)
            {
                if (p == null) continue;
                outList.Add(new ColonistEntry
                {
                    Id    = p.ThingID ?? "",
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
            return TopRoster(outList, RosterMax);
        }

        // The true top ten, not the first ten in map order: most kills, then longest service, then name so it is stable.
        internal static List<ColonistEntry> TopRoster(List<ColonistEntry> all, int max)
            => (all ?? new List<ColonistEntry>()).Where(c => c != null)
                .OrderByDescending(c => c.Kills).ThenByDescending(c => c.Days)
                .ThenBy(c => c.Name ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Name ?? "", StringComparer.Ordinal)
                .Take(Math.Max(0, max)).ToList();

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

        private static ColonistProfile BuildColonistProfile(Pawn p, Patches.GameComponent_KMHKillTally tally)
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

                d.HealthPct = Pct(p.health?.summaryHealth?.SummaryHealthPercent ?? 0f);
                d.PainPct   = Pct(p.health?.hediffSet?.PainTotal ?? 0f);
                d.Capacities = Capacities(p);
                d.Conditions = Conditions(p);

                d.TotalKills     = RecordInt(p, RecordDefOf.Kills);
                d.HumanlikeKills = RecordInt(p, RecordDefOf.KillsHumanlikes);
                d.MechanoidKills = RecordInt(p, RecordDefOf.KillsMechanoids);
                d.AnimalKills    = RecordInt(p, RecordDefOf.KillsAnimals);
                d.DamageTaken    = RecordInt(p, RecordDefOf.DamageTaken);
                ThingWithComps wep = p.equipment?.Primary;
                d.Weapon = wep?.LabelCap ?? "None";
                d.WeaponQuality = (wep != null && wep.TryGetQuality(out QualityCategory q)) ? q.GetLabel().CapitalizeFirst() : "None";
                d.RecentCombat  = tally?.RecentCombatOf(p.ThingID, RecentCombatMax) ?? new List<string>();
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

        // By defName, not DefOf, so version-specific capacities like Eating resolve and a missing one just drops out.
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
