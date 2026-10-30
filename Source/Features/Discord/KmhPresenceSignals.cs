using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace KMHPatch.Features.Discord
{
    // Main thread; reads only what RimWorld already keeps current, so sampling every few seconds stays cheap.
    internal static class KmhPresenceSignals
    {
        internal enum LordKind { None, Staging, Siege, MechCluster, Allies, Visitors, Traders, Ritual, Royal, PrisonBreak, Rebellion, FormCaravan, Transporters }

        // Lord jobs and conditions are told apart by class name, so a DLC or mod type never has to load for the check.
        private static readonly Dictionary<Type, LordKind> _lordKinds = new Dictionary<Type, LordKind>();
        private static readonly Dictionary<Type, PresenceKind> _conditionKinds = new Dictionary<Type, PresenceKind>();
        private static readonly List<GameCondition> _conditions = new List<GameCondition>();
        private static readonly HashSet<string> _failedProbes = new HashSet<string>(StringComparer.Ordinal);

        private static Game _game;
        private static int _homeMaps = -1;
        private static ResearchProjectDef _project;
        private static DateTime _researchDoneUntil = DateTime.MinValue, _settledUntil = DateTime.MinValue;
        private static bool _foodShort, _hot, _cold, _wasMultiplayer;
        private static DateTime _lastMultiplayerUtc = DateTime.MinValue;
        private static readonly Dictionary<int, int> _dropRaidTick = new Dictionary<int, int>();

        internal static readonly TimeSpan ResearchDoneFor = TimeSpan.FromMinutes(3);
        internal static readonly TimeSpan SettledFor = TimeSpan.FromMinutes(10);
        internal static readonly TimeSpan ReconnectWindow = TimeSpan.FromMinutes(10);
        internal const int DropRaidTicks = 10000;
        internal const int NewColonyTicks = 2 * GenDate.TicksPerDay;
        // Alert_LowFood stays quiet this long too.
        internal const int LowFoodGraceTicks = 150000;
        internal const PresenceTier ServerEventTier = PresenceTier.Normal;
        internal const PresenceTier FoodShortageTier = PresenceTier.Medium;

        public static bool Multiplayer
        {
            get
            {
                if (SubProtocol.KmhDispatcher.IsKmhServer) return true;
                return RwtCompat.NetworkState == "Connected";
            }
        }

        public static void Sample(List<PresenceCandidate> into, DateTime nowUtc, bool gameplay)
        {
            into.Clear();
            bool mp = Multiplayer;
            if (mp) _lastMultiplayerUtc = nowUtc;

            ProgramState state = Current.ProgramState;
            if (state != ProgramState.Playing || Current.Game == null)
            {
                _game = null;
                into.Add(new PresenceCandidate(Phase(state, nowUtc), PresenceTier.Normal));
                return;
            }

            if (!ReferenceEquals(_game, Current.Game)) NewGame();
            if (mp) _wasMultiplayer = true;
            else if (_wasMultiplayer) into.Add(new PresenceCandidate(PresenceKind.Disconnected, PresenceTier.High));

            if (!gameplay) return;
            Probe("maps", () => Maps(into, nowUtc));
            Probe("world", () => WorldSignals(into, nowUtc));
            Probe("server", () => Server(into));
            Probe("extensions", () => Extensions(into));
            if (into.Count == 0 || OnlyDisconnected(into)) into.Add(new PresenceCandidate(PresenceKind.Colony, PresenceTier.Normal));
        }

        private static bool OnlyDisconnected(List<PresenceCandidate> into)
            => into.Count == 1 && into[0].Kind == PresenceKind.Disconnected;

        private static PresenceKind Phase(ProgramState state, DateTime nowUtc)
        {
            if (KMHPatch.Features.Mods.KmhJoinPreparation.Active)
                return KMHPatch.Features.Mods.KmhJoinPreparation.Connecting ? PresenceKind.Connecting : PresenceKind.PreparingMods;
            if (RwtCompat.NetworkState == "Connecting")
                return nowUtc - _lastMultiplayerUtc <= ReconnectWindow && _lastMultiplayerUtc != DateTime.MinValue
                    ? PresenceKind.Reconnecting : PresenceKind.Connecting;
            if (state == ProgramState.MapInitializing || LongEventHandler.AnyEventNowOrWaiting || Multiplayer) return PresenceKind.Loading;
            return PresenceKind.MainMenu;
        }

        private static void NewGame()
        {
            _game = Current.Game;
            _homeMaps = -1;
            _project = null;
            _researchDoneUntil = _settledUntil = DateTime.MinValue;
            _foodShort = _hot = _cold = _wasMultiplayer = false;
            _dropRaidTick.Clear();
        }

        internal static int FailedProbeCount => _failedProbes.Count;

        // One failing probe must not blank the status, and must not log every two seconds.
        private static void Probe(string name, Action probe)
        {
            try { probe(); }
            catch (Exception ex)
            {
                if (_failedProbes.Add(name)) KmhLog.Warn($"Discord: the '{name}' activity check failed and is skipped ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private static void Maps(List<PresenceCandidate> into, DateTime nowUtc)
        {
            int homes = 0;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null) continue;
                bool home = map.IsPlayerHome;
                int colonists = map.mapPawns.FreeColonistsSpawnedCount;
                if (!home && colonists == 0) continue;
                Threats(into, map, home);
                if (home) { homes++; Home(into, map, colonists, homes == 1); }
                else Away(into, map);
            }

            // Counted from the second sample on, so loading a save is not mistaken for founding one.
            if (_homeMaps >= 1 && homes > _homeMaps) _settledUntil = nowUtc + SettledFor;
            _homeMaps = homes;
            if (nowUtc < _settledUntil) into.Add(new PresenceCandidate(PresenceKind.NewSettlement, PresenceTier.Medium));
        }

        private static void Threats(List<PresenceCandidate> into, Map map, bool home)
        {
            int raiders = 0, mechs = 0, insects = 0, manhunters = 0, entities = 0;
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToColony)
            {
                if (!GenHostility.IsActiveThreatToPlayer(t, false)) continue;
                if (!(t.Thing is Pawn p) || p.RaceProps == null) { raiders++; continue; }
                if (p.RaceProps.IsAnomalyEntity) entities++;
                else if (p.RaceProps.IsMechanoid) mechs++;
                else if (p.RaceProps.Insect) insects++;
                else if (p.RaceProps.Animal && IsManhunter(p)) manhunters++;
                else raiders++;
            }

            bool siege = false, staging = false, cluster = false, allies = false, prison = false, rebellion = false;
            List<Lord> lords = map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                LordKind kind = ClassifyLord(lord?.LordJob?.GetType());
                if (kind == LordKind.None) continue;
                bool hostile = lord.faction != null && lord.faction.HostileTo(Faction.OfPlayer);
                bool mine = lord.faction != null && lord.faction == Faction.OfPlayer;
                switch (kind)
                {
                    case LordKind.Siege:        if (hostile) siege = true; break;
                    case LordKind.Staging:      if (hostile && IsStaging(lord)) staging = true; break;
                    case LordKind.MechCluster:  if (hostile) cluster = true; break;
                    case LordKind.Allies:       if (!hostile) allies = true; break;
                    case LordKind.PrisonBreak:  prison = true; break;
                    case LordKind.Rebellion:    rebellion = true; break;
                    case LordKind.Visitors:     if (home && !hostile) into.Add(new PresenceCandidate(PresenceKind.Visitors, PresenceTier.Medium)); break;
                    case LordKind.Traders:      if (home && !hostile) into.Add(new PresenceCandidate(PresenceKind.TraderVisit, PresenceTier.Medium)); break;
                    case LordKind.Ritual:       if (mine) into.Add(new PresenceCandidate(PresenceKind.Ritual, PresenceTier.Medium)); break;
                    case LordKind.Royal:        if (mine) into.Add(new PresenceCandidate(PresenceKind.RoyalCeremony, PresenceTier.Medium)); break;
                    case LordKind.FormCaravan:  if (mine) into.Add(new PresenceCandidate(PresenceKind.FormingCaravan, PresenceTier.Medium)); break;
                    case LordKind.Transporters: if (mine) into.Add(new PresenceCandidate(PresenceKind.TransportPods, PresenceTier.Medium)); break;
                }
            }

            if (prison) into.Add(new PresenceCandidate(PresenceKind.PrisonBreak, PresenceTier.Critical));
            if (rebellion) into.Add(new PresenceCandidate(PresenceKind.SlaveRebellion, PresenceTier.Critical));

            int hostiles = LoneAnimal(raiders, mechs, insects, manhunters, entities) ? 0 : raiders + mechs + insects + manhunters + entities;
            if (hostiles == 0)
            {
                if (!home) return;
                if (siege) into.Add(new PresenceCandidate(PresenceKind.Siege, PresenceTier.High));
                else if (staging) into.Add(new PresenceCandidate(PresenceKind.RaidApproaching, PresenceTier.High));
                else if (cluster) into.Add(new PresenceCandidate(PresenceKind.MechCluster, PresenceTier.High));
                return;
            }

            PresenceTier tier = hostiles >= 3 || map.dangerWatcher.DangerRating == StoryDanger.High ? PresenceTier.Critical : PresenceTier.High;
            PresenceKind fight = !home ? PresenceKind.AwayFight
                : siege ? PresenceKind.Siege
                : RaidKind(raiders, mechs, insects, manhunters, entities, RecentDropRaid(map));
            into.Add(new PresenceCandidate(fight, tier) { WithAllies = allies });
        }

        // A hunted animal taking revenge is not a manhunter pack, and leaves no raid to recover from.
        internal static bool LoneAnimal(int raiders, int mechs, int insects, int manhunters, int entities)
            => manhunters == 1 && raiders + mechs + insects + entities == 0;

        // Whoever the raid mostly is; ties go to the stranger threat.
        internal static PresenceKind RaidKind(int raiders, int mechs, int insects, int manhunters, int entities, bool dropPods)
        {
            int top = Math.Max(Math.Max(raiders, mechs), Math.Max(Math.Max(insects, manhunters), entities));
            if (top <= 0) return PresenceKind.Raid;
            if (entities == top) return PresenceKind.RaidEntities;
            if (mechs == top) return PresenceKind.RaidMechanoids;
            if (insects == top) return PresenceKind.RaidInsects;
            if (manhunters == top) return PresenceKind.RaidManhunters;
            return dropPods ? PresenceKind.DropPods : PresenceKind.Raid;
        }

        private static bool IsManhunter(Pawn p)
        {
            MentalStateDef m = p.MentalStateDef;
            return m != null && (m == MentalStateDefOf.Manhunter || m == MentalStateDefOf.ManhunterPermanent);
        }

        private static bool IsStaging(Lord lord)
        {
            for (Type t = lord.CurLordToil?.GetType(); t != null && t != typeof(object); t = t.BaseType)
                if (t.Name == "LordToil_Stage") return true;
            return false;
        }

        internal static LordKind ClassifyLord(Type job)
        {
            if (job == null) return LordKind.None;
            if (_lordKinds.TryGetValue(job, out LordKind cached)) return cached;
            LordKind kind = LordKind.None;
            for (Type t = job; t != null && t != typeof(object) && kind == LordKind.None; t = t.BaseType)
                kind = LordKindByName(t.Name);
            _lordKinds[job] = kind;
            return kind;
        }

        internal static LordKind LordKindByName(string name)
        {
            switch (name)
            {
                case "LordJob_Siege": return LordKind.Siege;
                case "LordJob_StageThenAttack": return LordKind.Staging;
                case "LordJob_MechanoidDefendBase": return LordKind.MechCluster;

                case "LordJob_AssistColony": return LordKind.Allies;
                case "LordJob_VisitColony": return LordKind.Visitors;
                case "LordJob_TradeWithColony": return LordKind.Traders;
                case "LordJob_BestowingCeremony": return LordKind.Royal;
                case "LordJob_Ritual": case "LordJob_Joinable_Party": case "LordJob_Joinable_MarriageCeremony":
                case "LordJob_Joinable_Gathering": case "LordJob_PsychicRitual":
                    return LordKind.Ritual;
                case "LordJob_PrisonBreak": return LordKind.PrisonBreak;
                case "LordJob_SlaveRebellion": return LordKind.Rebellion;
                case "LordJob_FormAndSendCaravan": return LordKind.FormCaravan;
                case "LordJob_LoadAndEnterTransporters": return LordKind.Transporters;
                default: return LordKind.None;
            }
        }

        private static void Home(List<PresenceCandidate> into, Map map, int colonists, bool primary)
        {
            if (map.fireWatcher.LargeFireDangerPresent) into.Add(new PresenceCandidate(PresenceKind.Fire, PresenceTier.Critical));

            int downed = 0, sick = 0;
            List<Pawn> pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Dead || p.health == null) continue;
                if (p.Downed && !IsBaby(p)) downed++;
                if (IsSick(p)) sick++;
            }
            PresenceKind medical = MedicalKind(downed, colonists);
            if (medical == PresenceKind.MedicalEmergency) into.Add(new PresenceCandidate(medical, PresenceTier.Critical));
            else if (medical == PresenceKind.InjuredColonists) into.Add(new PresenceCandidate(medical, PresenceTier.High));
            if (Outbreak(sick, colonists)) into.Add(new PresenceCandidate(PresenceKind.Disease, PresenceTier.High));

            int age = Find.TickManager.TicksGame - map.generationTick;
            if (primary)
            {
                _foodShort = FoodShort(map.resourceCounter.TotalHumanEdibleNutrition, colonists, _foodShort)
                          && age >= LowFoodGraceTicks;
                // RimWorld's Low food alert is minor; at High it would hide everything else for whole early games.
                if (_foodShort) into.Add(new PresenceCandidate(PresenceKind.FoodShortage, FoodShortageTier));
            }

            bool tempCondition = Conditions(into, map);

            float outdoor = map.mapTemperature.OutdoorTemp;
            if (primary)
            {
                _hot = Hot(outdoor, _hot);
                _cold = Cold(outdoor, _cold);
            }
            if (primary && !tempCondition && _hot) into.Add(new PresenceCandidate(PresenceKind.ExtremeHeat, PresenceTier.Medium));
            if (primary && !tempCondition && _cold) into.Add(new PresenceCandidate(PresenceKind.ExtremeCold, PresenceTier.Medium));

            if (!primary) return;
            if (age < NewColonyTicks)
            {
                into.Add(new PresenceCandidate(PresenceKind.NewColony, PresenceTier.Normal));
                return;
            }
            into.Add(new PresenceCandidate(PresenceKind.Colony, PresenceTier.Normal));
            Season season = GenLocalDate.Season(map);
            if (season == Season.Winter || season == Season.PermanentWinter)
            {
                if (outdoor < -15f && !_cold) into.Add(new PresenceCandidate(PresenceKind.HarshWinter, PresenceTier.Medium));
                else into.Add(new PresenceCandidate(PresenceKind.WinterLife, PresenceTier.Normal));
            }
            else if (season == Season.Fall) into.Add(new PresenceCandidate(PresenceKind.PrepareWinter, PresenceTier.Normal));
            else if (season == Season.Spring || season == Season.Summer) into.Add(new PresenceCandidate(PresenceKind.GrowingSeason, PresenceTier.Normal));
            if (colonists >= 10) into.Add(new PresenceCandidate(PresenceKind.GrowingColony, PresenceTier.Normal));
            if (Find.ResearchManager?.GetProject(null) != null) into.Add(new PresenceCandidate(PresenceKind.Research, PresenceTier.Normal));
            if (ModsConfig.BiotechActive && map.mapPawns.SpawnedColonyMechs.Count >= 3)
                into.Add(new PresenceCandidate(PresenceKind.Mechanoids, PresenceTier.Normal));
            if (ModsConfig.AnomalyActive && HoldsEntity(map)) into.Add(new PresenceCandidate(PresenceKind.Entities, PresenceTier.Normal));
            if (ModsConfig.OdysseyActive && map.Tile.LayerDef != null && map.Tile.LayerDef.isSpace)
                into.Add(new PresenceCandidate(PresenceKind.Orbit, PresenceTier.Normal));
        }

        private static bool IsBaby(Pawn p) => (p.DevelopmentalStage & DevelopmentalStage.Baby) != 0
                                           || (p.DevelopmentalStage & DevelopmentalStage.Newborn) != 0;

        private static bool IsSick(Pawn p)
        {
            List<Hediff> hediffs = p.health.hediffSet?.hediffs;
            if (hediffs == null) return false;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                if (h?.def != null && IsIllness(h.def.makesSickThought, h.def.tendable) && h.Visible) return true;
            }
            return false;
        }

        internal static PresenceKind MedicalKind(int downed, int colonists)
        {
            if (downed <= 0 || colonists <= 0) return PresenceKind.None;
            if (downed >= 2 && downed * 2 >= colonists) return PresenceKind.MedicalEmergency;
            return PresenceKind.InjuredColonists;
        }

        // Cryptosleep sickness, morning sickness and toxic buildup also make a sick thought, and are no outbreak.
        internal static bool IsIllness(bool makesSickThought, bool tendable) => makesSickThought && tendable;

        internal static bool Outbreak(int sick, int colonists)
            => sick >= 3 || (sick >= 2 && colonists > 0 && sick * 4 >= colonists);

        // RimWorld's low-food threshold, with hysteresis so the status does not flicker at the line.
        internal static bool FoodShort(float nutrition, int colonists, bool wasShort)
        {
            if (colonists <= 0) return false;
            return wasShort ? nutrition < 6f * colonists : nutrition < 4f * colonists;
        }

        internal static bool Hot(float outdoor, bool was) => was ? outdoor > 42f : outdoor > 45f;
        internal static bool Cold(float outdoor, bool was) => was ? outdoor < -27f : outdoor < -30f;

        private static bool HoldsEntity(Map map)
        {
            List<Thing> holders = map.listerThings.ThingsInGroup(ThingRequestGroup.EntityHolder);
            for (int i = 0; i < holders.Count; i++)
                if (holders[i] is Building_HoldingPlatform platform && platform.Occupied) return true;
            return false;
        }

        // True when a condition already explains the temperature, so heat is not reported twice.
        private static bool Conditions(List<PresenceCandidate> into, Map map)
        {
            _conditions.Clear();
            map.gameConditionManager.GetAllGameConditionsAffectingMap(map, _conditions);
            bool temperature = false;
            for (int i = 0; i < _conditions.Count; i++)
            {
                GameCondition c = _conditions[i];
                if (c?.def == null || c.Expired) continue;
                PresenceKind kind = HazardKind(c.def);
                if (kind == PresenceKind.None) continue;
                if (kind == PresenceKind.HeatWave || kind == PresenceKind.ColdSnap) temperature = true;
                string label = CleanLabel(c.def.label);
                if (c.Permanent) into.Add(new PresenceCandidate(PresenceKind.LastingHazard, PresenceTier.Normal, label));
                else into.Add(new PresenceCandidate(kind, PresenceTier.High, label));
            }
            return temperature;
        }

        internal static bool IsBad(GameConditionDef def)
        {
            LetterDef l = def?.letterDef;
            return l != null && (l == LetterDefOf.NegativeEvent || l == LetterDefOf.ThreatBig || l == LetterDefOf.ThreatSmall);
        }

        // A condition announced as good is never a hazard: psychic soothe shares the drone's class.
        internal static PresenceKind HazardKind(GameConditionDef def)
        {
            if (def == null || def.letterDef == LetterDefOf.PositiveEvent) return PresenceKind.None;
            PresenceKind kind = ClassifyCondition(def);
            if (kind != PresenceKind.None) return kind;
            return IsBad(def) ? PresenceKind.Hazard : PresenceKind.None;
        }

        internal static PresenceKind ClassifyCondition(GameConditionDef def)
        {
            Type cls = def?.conditionClass;
            if (cls == null) return PresenceKind.None;
            if (_conditionKinds.TryGetValue(cls, out PresenceKind cached)) return cached;
            PresenceKind kind = PresenceKind.None;
            for (Type t = cls; t != null && t != typeof(object) && kind == PresenceKind.None; t = t.BaseType)
                kind = ConditionKindByName(t.Name);
            _conditionKinds[cls] = kind;
            return kind;
        }

        internal static PresenceKind ConditionKindByName(string name)
        {
            switch (name)
            {
                case "GameCondition_ToxicFallout": return PresenceKind.ToxicFallout;
                case "GameCondition_VolcanicWinter": return PresenceKind.VolcanicWinter;
                case "GameCondition_DisableElectricity": return PresenceKind.SolarFlare;
                case "GameCondition_NoSunlight": return PresenceKind.Eclipse;
                case "GameCondition_PsychicEmanation": return PresenceKind.PsychicDrone;
                case "GameCondition_HeatWave": return PresenceKind.HeatWave;
                case "GameCondition_ColdSnap": return PresenceKind.ColdSnap;
                case "GameCondition_Flashstorm": return PresenceKind.Flashstorm;
                case "GameCondition_NoxiousHaze": return PresenceKind.NoxiousHaze;
                // Anomaly and Odyssey hazards that send no letter of their own; named by their own label.
                case "GameCondition_NoSunlight_Instant": case "GameCondition_UnnaturalDarkness": case "GameCondition_BloodRain":
                case "GameCondition_DeathPall": case "GameCondition_HateChantDrone": case "GameCondition_UnnaturalHeat":
                case "GameCondition_LavaFlow": case "GameCondition_VolcanicAsh": case "GameCondition_VolcanicDebrisShower":
                case "GameCondition_Drought": case "GameCondition_GillRot": case "GameCondition_HeatVentHeat":
                case "GameCondition_Planetkiller":
                    return PresenceKind.Hazard;
                default: return PresenceKind.None;
            }
        }

        // A def label is mod-supplied text: markup and control characters stay out of Discord.
        internal static string CleanLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return UI.KmhText.Get("KMH_RP_HazardFallback", "a disaster");
            var sb = new System.Text.StringBuilder(label.Length);
            bool inTag = false;
            foreach (char ch in label)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>') { inTag = false; continue; }
                if (!inTag && !char.IsControl(ch)) sb.Append(ch);
            }
            string clean = KmhDiscordRpc.Trim(sb.ToString(), 48);
            return clean.Length == 0 ? UI.KmhText.Get("KMH_RP_HazardFallback", "a disaster") : clean;
        }

        private static void Away(List<PresenceCandidate> into, Map map)
        {
            MapParent parent = map.Parent;
            if (parent is Site site)
            {
                bool ruins = false;
                if (site.parts != null)
                    foreach (SitePart part in site.parts)
                        if (part?.def?.defName != null && part.def.defName.StartsWith("AncientComplex", StringComparison.Ordinal)) ruins = true;
                into.Add(new PresenceCandidate(ruins ? PresenceKind.AncientRuins : PresenceKind.QuestSite, PresenceTier.Medium));
            }
            else if (parent is Settlement s && s.Faction != null && s.Faction.HostileTo(Faction.OfPlayer))
                into.Add(new PresenceCandidate(PresenceKind.AttackBase, PresenceTier.High));
            else into.Add(new PresenceCandidate(PresenceKind.Expedition, PresenceTier.Medium));
        }

        private static void WorldSignals(List<PresenceCandidate> into, DateTime nowUtc)
        {
            bool moving = false, visiting = false, any = false;
            List<Caravan> caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan c = caravans[i];
                if (c == null || !c.IsPlayerControlled) continue;
                any = true;
                if (CaravanVisitUtility.SettlementVisitedNow(c) != null) visiting = true;
                else if (c.pather != null && c.pather.Moving) moving = true;
            }
            if (visiting) into.Add(new PresenceCandidate(PresenceKind.CaravanVisit, PresenceTier.Medium));
            else if (moving) into.Add(new PresenceCandidate(PresenceKind.CaravanTravel, PresenceTier.Medium));
            if (any && _homeMaps == 0) into.Add(new PresenceCandidate(PresenceKind.Nomads, PresenceTier.Normal));

            if (ModsConfig.OdysseyActive)
            {
                List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] is Gravship) { into.Add(new PresenceCandidate(PresenceKind.Gravship, PresenceTier.Medium)); break; }
            }

            if (WorldRendererUtility.WorldSelected) into.Add(new PresenceCandidate(PresenceKind.WorldMap, PresenceTier.Medium));
            if (TradeSession.Active) into.Add(new PresenceCandidate(PresenceKind.Trading, PresenceTier.Medium));
            if (Find.WindowStack != null && Find.WindowStack.IsOpen(typeof(Dialog_FormCaravan)))
                into.Add(new PresenceCandidate(PresenceKind.FormingCaravan, PresenceTier.Medium));
            if (ShipCountdown.CountingDown) into.Add(new PresenceCandidate(PresenceKind.ShipLaunch, PresenceTier.High));

            ResearchProjectDef current = Find.ResearchManager?.GetProject(null);
            if (_project != null && _project != current && _project.IsFinished) _researchDoneUntil = nowUtc + ResearchDoneFor;
            _project = current;
            if (nowUtc < _researchDoneUntil) into.Add(new PresenceCandidate(PresenceKind.ResearchDone, PresenceTier.Medium));
        }

        private static void Server(List<PresenceCandidate> into)
        {
            // A server KMH cannot confirm is published gets no detail about what the player does on it.
            if (!SubProtocol.KmhDispatcher.IsKmhServer || KmhRichPresence.CardForCurrentServer() == null) return;
            KmhActivity a = KmhPresenceActivity.Current;
            if (a != KmhActivity.None)
                into.Add(new PresenceCandidate(PresenceKind.ServerScreen, PresenceTier.Medium, KmhPresenceActivity.Line(a)));
            // Background, not something the player is doing: at Medium an event outranks the colony for the whole session.
            string ev = EventToShow(KMHPatch.Features.World.WorldCache.ActiveEvents());
            if (ev != null) into.Add(new PresenceCandidate(PresenceKind.ServerEvent, ServerEventTier, ev));
        }

        // World weather already shows as the condition it brings; otherwise the newest event names the line.
        internal static string EventToShow(List<KMHPatch.Features.World.Dto.WorldEventDto> active)
        {
            KMHPatch.Features.World.Dto.WorldEventDto best = null;
            foreach (var e in active)
                if (e != null && e.Type != KMHPatch.Features.World.Dto.WorldEventDto.WorldWeather
                    && (best == null || e.StartedUtcTicks > best.StartedUtcTicks)) best = e;
            return best == null ? null : best.Type ?? "";
        }

        private static bool _watchingProviders;

        private static void Extensions(List<PresenceCandidate> into)
        {
            if (!_watchingProviders)
            {
                _watchingProviders = true;
                KMH.Sdk.Client.Apis.PresenceProviders.ProviderDisabled += id => KmhLog.Warn($"Discord: activity provider '{id}' kept failing and was turned off.");
            }
            foreach (KMH.Sdk.Client.Apis.PresenceClaim claim in KMH.Sdk.Client.Apis.PresenceProviders.Collect())
            {
                string details = CleanExtensionText(claim?.Details);
                if (details.Length == 0) continue;
                PresenceTier tier = (PresenceTier)Math.Max(0, Math.Min(3, (int)claim.Priority));
                string state = CleanExtensionText(claim.State);
                into.Add(new PresenceCandidate(PresenceKind.Extension, tier, details, state.Length == 0 ? null : state));
            }
        }

        internal static string CleanExtensionText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            bool inTag = false;
            foreach (char ch in text)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(ch);
            }
            return KmhDiscordRpc.Trim(sb.ToString(), 64);
        }

        internal static void NoteDropRaid(Map map, int tick)
        {
            if (map != null) _dropRaidTick[map.uniqueID] = tick;
        }

        private static bool RecentDropRaid(Map map)
            => _dropRaidTick.TryGetValue(map.uniqueID, out int tick) && Find.TickManager.TicksGame - tick <= DropRaidTicks;

        internal static void ResetForTest()
        {
            _game = null;
            NewGame();
            _game = null;
            _lastMultiplayerUtc = DateTime.MinValue;
        }
    }
}
