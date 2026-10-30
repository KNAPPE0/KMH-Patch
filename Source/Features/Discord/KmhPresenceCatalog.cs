using System;

namespace KMHPatch.Features.Discord
{
    // Fixed, translatable text only: {0} is a game label, never player input.
    internal static class KmhPresenceCatalog
    {
        internal static readonly TimeSpan BaselineVariantEvery = TimeSpan.FromMinutes(10);
        internal static readonly TimeSpan EventVariantEvery = TimeSpan.FromMinutes(5);

        // Art asset names exactly as uploaded to the Discord application; a missing one simply draws nothing.
        internal static string AssetFor(PresenceIcon icon)
        {
            switch (icon)
            {
                case PresenceIcon.Combat:      return "cn_combat_v1";
                case PresenceIcon.Hazard:      return "cn_hazard_v1";
                case PresenceIcon.Emergency:   return "cn_emergency_v1";
                case PresenceIcon.Caravan:     return "cn_caravan_v1";
                case PresenceIcon.Colony:      return "cn_colony_v1";
                case PresenceIcon.Research:    return "cn_research_v1";
                case PresenceIcon.Multiplayer: return "cn_multiplayer_v1";
                default:                       return "";
            }
        }

        internal static string IconText(PresenceIcon icon)
        {
            switch (icon)
            {
                case PresenceIcon.Combat:      return UI.KmhText.Get("KMH_RP_IconCombat", "In combat");
                case PresenceIcon.Hazard:      return UI.KmhText.Get("KMH_RP_IconHazard", "Environmental hazard");
                case PresenceIcon.Emergency:   return UI.KmhText.Get("KMH_RP_IconEmergency", "Emergency");
                case PresenceIcon.Caravan:     return UI.KmhText.Get("KMH_RP_IconCaravan", "Out in the world");
                case PresenceIcon.Colony:      return UI.KmhText.Get("KMH_RP_IconColony", "Colony life");
                case PresenceIcon.Research:    return UI.KmhText.Get("KMH_RP_IconResearch", "Research");
                case PresenceIcon.Multiplayer: return UI.KmhText.Get("KMH_RP_IconMultiplayer", "Multiplayer");
                default:                       return "";
            }
        }

        internal static PresenceIcon IconFor(PresenceKind k)
        {
            switch (k)
            {
                case PresenceKind.Raid: case PresenceKind.RaidMechanoids: case PresenceKind.RaidInsects:
                case PresenceKind.RaidManhunters: case PresenceKind.RaidEntities: case PresenceKind.Siege:
                case PresenceKind.DropPods: case PresenceKind.AwayFight: case PresenceKind.RaidApproaching:
                case PresenceKind.MechCluster: case PresenceKind.PrisonBreak: case PresenceKind.SlaveRebellion:
                case PresenceKind.AttackBase:
                    return PresenceIcon.Combat;
                case PresenceKind.ToxicFallout: case PresenceKind.VolcanicWinter: case PresenceKind.SolarFlare:
                case PresenceKind.Eclipse: case PresenceKind.PsychicDrone: case PresenceKind.HeatWave:
                case PresenceKind.ColdSnap: case PresenceKind.Flashstorm: case PresenceKind.NoxiousHaze:
                case PresenceKind.Hazard: case PresenceKind.LastingHazard: case PresenceKind.ExtremeHeat:
                case PresenceKind.ExtremeCold: case PresenceKind.HarshWinter:
                    return PresenceIcon.Hazard;
                case PresenceKind.Fire: case PresenceKind.MedicalEmergency: case PresenceKind.InjuredColonists:
                case PresenceKind.Disease: case PresenceKind.FoodShortage: case PresenceKind.Recovering:
                    return PresenceIcon.Emergency;
                case PresenceKind.WorldMap: case PresenceKind.CaravanTravel: case PresenceKind.CaravanVisit:
                case PresenceKind.Trading: case PresenceKind.FormingCaravan: case PresenceKind.TransportPods:
                case PresenceKind.Expedition: case PresenceKind.QuestSite: case PresenceKind.AncientRuins:
                case PresenceKind.Gravship: case PresenceKind.Nomads: case PresenceKind.Orbit:
                    return PresenceIcon.Caravan;
                case PresenceKind.Research: case PresenceKind.ResearchDone: case PresenceKind.ShipLaunch:
                    return PresenceIcon.Research;
                case PresenceKind.ServerScreen: case PresenceKind.ServerEvent:
                    return PresenceIcon.Multiplayer;
                case PresenceKind.None: case PresenceKind.MainMenu: case PresenceKind.Connecting:
                case PresenceKind.Reconnecting: case PresenceKind.PreparingMods: case PresenceKind.Loading:
                case PresenceKind.Disconnected:
                    return PresenceIcon.None;
                default:
                    return PresenceIcon.Colony;
            }
        }

        internal static string[] Details(PresenceKind k, string label)
        {
            switch (k)
            {
                case PresenceKind.MainMenu:      return new[] { UI.KmhText.Get("KMH_RP_Menu", "In the main menu") };
                case PresenceKind.Connecting:    return new[] { UI.KmhText.Get("KMH_RP_Connecting", "Connecting to a server") };
                case PresenceKind.Reconnecting:  return new[] { UI.KmhText.Get("KMH_RP_Reconnecting", "Reconnecting to the server") };
                case PresenceKind.PreparingMods: return new[] { UI.KmhText.Get("KMH_RP_PreparingMods", "Preparing mods to join a server") };
                case PresenceKind.Loading:       return new[] { UI.KmhText.Get("KMH_RP_Loading", "Loading a colony") };
                case PresenceKind.Disconnected:  return new[] { UI.KmhText.Get("KMH_RP_Disconnected", "Disconnected from the server") };

                case PresenceKind.Colony: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Colony1", "Managing the colony"),
                    UI.KmhText.Get("KMH_RP_Colony2", "Life on the Rim"),
                    UI.KmhText.Get("KMH_RP_Colony3", "Another day on the Rim"),
                    UI.KmhText.Get("KMH_RP_Colony4", "Keeping the colony running"),
                };
                case PresenceKind.NewColony: return new[]
                {
                    UI.KmhText.Get("KMH_RP_NewColony1", "Founding a new colony"),
                    UI.KmhText.Get("KMH_RP_NewColony2", "Building a new settlement"),
                    UI.KmhText.Get("KMH_RP_NewColony3", "Establishing a new colony"),
                };
                case PresenceKind.GrowingColony: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Growing1", "Managing a growing colony"),
                    UI.KmhText.Get("KMH_RP_Growing2", "Running a busy settlement"),
                };
                case PresenceKind.GrowingSeason: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Season1", "Growing season on the Rim"),
                    UI.KmhText.Get("KMH_RP_Season2", "Tending the fields"),
                };
                case PresenceKind.PrepareWinter: return new[]
                {
                    UI.KmhText.Get("KMH_RP_PrepWinter1", "Preparing for winter"),
                    UI.KmhText.Get("KMH_RP_PrepWinter2", "Stocking up before winter"),
                };
                case PresenceKind.WinterLife: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Winter1", "Weathering the winter"),
                    UI.KmhText.Get("KMH_RP_Winter2", "Waiting out the winter"),
                };
                case PresenceKind.Research: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Research1", "Researching new technology"),
                    UI.KmhText.Get("KMH_RP_Research2", "Advancing the colony's research"),
                    UI.KmhText.Get("KMH_RP_Research3", "Studying new technology"),
                };
                case PresenceKind.Mechanoids: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Mechs1", "Overseeing a mechanoid workforce"),
                    UI.KmhText.Get("KMH_RP_Mechs2", "Commanding colony mechanoids"),
                };
                case PresenceKind.Entities: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Entities1", "Studying captured entities"),
                    UI.KmhText.Get("KMH_RP_Entities2", "Containing anomalies"),
                };
                case PresenceKind.Nomads: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Nomads1", "Traveling across the Rim"),
                    UI.KmhText.Get("KMH_RP_Nomads2", "Living as nomads"),
                };
                case PresenceKind.Orbit: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Orbit1", "Operating in orbit"),
                    UI.KmhText.Get("KMH_RP_Orbit2", "Exploring orbit"),
                };
                case PresenceKind.LastingHazard: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Lasting1", "Enduring {0}", label),
                    UI.KmhText.Get("KMH_RP_Lasting2", "Living with {0}", label),
                };

                case PresenceKind.WorldMap: return new[]
                {
                    UI.KmhText.Get("KMH_RP_World1", "Exploring the world map"),
                    UI.KmhText.Get("KMH_RP_World2", "Planning the next journey"),
                };
                case PresenceKind.CaravanTravel: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Caravan1", "Traveling across the Rim"),
                    UI.KmhText.Get("KMH_RP_Caravan2", "Leading a caravan"),
                    UI.KmhText.Get("KMH_RP_Caravan3", "Caravan on the move"),
                };
                case PresenceKind.CaravanVisit: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Visit1", "Visiting a settlement"),
                    UI.KmhText.Get("KMH_RP_Visit2", "Caravan at a settlement"),
                };
                case PresenceKind.Trading: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Trade1", "Trading with a faction"),
                    UI.KmhText.Get("KMH_RP_Trade2", "Striking a deal"),
                };
                case PresenceKind.FormingCaravan: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Forming1", "Preparing a caravan"),
                    UI.KmhText.Get("KMH_RP_Forming2", "Packing for a journey"),
                };
                case PresenceKind.TransportPods: return new[] { UI.KmhText.Get("KMH_RP_Pods1", "Preparing transport pods") };
                case PresenceKind.Expedition: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Expedition1", "Exploring new lands"),
                    UI.KmhText.Get("KMH_RP_Expedition2", "On an expedition"),
                };
                case PresenceKind.QuestSite: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Quest1", "Exploring a quest site"),
                    UI.KmhText.Get("KMH_RP_Quest2", "Investigating a quest location"),
                };
                case PresenceKind.AncientRuins: return new[] { UI.KmhText.Get("KMH_RP_Ruins1", "Exploring ancient ruins") };
                case PresenceKind.Gravship: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Gravship1", "Flying the gravship"),
                    UI.KmhText.Get("KMH_RP_Gravship2", "Traveling by gravship"),
                };
                case PresenceKind.Visitors: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Visitors1", "Hosting visitors"),
                    UI.KmhText.Get("KMH_RP_Visitors2", "Welcoming visitors"),
                };
                case PresenceKind.TraderVisit: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Traders1", "Trading with a passing caravan"),
                    UI.KmhText.Get("KMH_RP_Traders2", "Traders at the colony"),
                };
                case PresenceKind.Ritual: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Ritual1", "Holding a ceremony"),
                    UI.KmhText.Get("KMH_RP_Ritual2", "Gathering for a ritual"),
                    UI.KmhText.Get("KMH_RP_Ritual3", "Celebrating together"),
                };
                case PresenceKind.RoyalCeremony: return new[] { UI.KmhText.Get("KMH_RP_Royal1", "Hosting a royal ceremony") };
                case PresenceKind.ResearchDone: return new[]
                {
                    UI.KmhText.Get("KMH_RP_ResearchDone1", "Completed a research project"),
                    UI.KmhText.Get("KMH_RP_ResearchDone2", "Celebrating a breakthrough"),
                };
                case PresenceKind.NewSettlement: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Settled1", "Established a new settlement"),
                    UI.KmhText.Get("KMH_RP_Settled2", "Expanding across the Rim"),
                };
                case PresenceKind.ExtremeHeat: return new[] { UI.KmhText.Get("KMH_RP_Heat1", "Surviving extreme heat") };
                case PresenceKind.ExtremeCold: return new[] { UI.KmhText.Get("KMH_RP_Cold1", "Surviving extreme cold") };
                case PresenceKind.HarshWinter: return new[] { UI.KmhText.Get("KMH_RP_HarshWinter1", "Surviving a harsh winter") };
                case PresenceKind.ServerEvent: return new[] { ServerEventLine(label) };
                case PresenceKind.ServerScreen:
                case PresenceKind.Extension:
                    return new[] { label ?? "" };

                case PresenceKind.RaidApproaching: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Approach1", "Raiders approaching"),
                    UI.KmhText.Get("KMH_RP_Approach2", "Hostiles gathering nearby"),
                    UI.KmhText.Get("KMH_RP_Approach3", "Bracing for an attack"),
                };
                case PresenceKind.MechCluster: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Cluster1", "Dealing with a mechanoid cluster"),
                    UI.KmhText.Get("KMH_RP_Cluster2", "A mechanoid cluster looms"),
                };
                case PresenceKind.Recovering: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Recover1", "Recovering after a raid"),
                    UI.KmhText.Get("KMH_RP_Recover2", "Cleaning up after the battle"),
                    UI.KmhText.Get("KMH_RP_Recover3", "Rebuilding after an attack"),
                };
                case PresenceKind.InjuredColonists: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Injured1", "Treating injured colonists"),
                    UI.KmhText.Get("KMH_RP_Injured2", "Tending to the wounded"),
                };
                case PresenceKind.Disease: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Disease1", "Fighting a disease outbreak"),
                    UI.KmhText.Get("KMH_RP_Disease2", "Battling an illness"),
                };
                case PresenceKind.FoodShortage: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Food1", "Facing a food shortage"),
                    UI.KmhText.Get("KMH_RP_Food2", "Running low on food"),
                };
                case PresenceKind.ShipLaunch: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Ship1", "Launching the ship"),
                    UI.KmhText.Get("KMH_RP_Ship2", "Counting down to launch"),
                };
                case PresenceKind.AttackBase: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Assault1", "Assaulting an enemy settlement"),
                    UI.KmhText.Get("KMH_RP_Assault2", "Leading an assault"),
                };
                case PresenceKind.ToxicFallout: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Toxic1", "Surviving toxic fallout"),
                    UI.KmhText.Get("KMH_RP_Toxic2", "Sheltering from toxic fallout"),
                };
                case PresenceKind.VolcanicWinter: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Volcanic1", "Weathering a volcanic winter"),
                    UI.KmhText.Get("KMH_RP_Volcanic2", "Living under ash clouds"),
                };
                case PresenceKind.SolarFlare: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Flare1", "Dealing with a solar flare"),
                    UI.KmhText.Get("KMH_RP_Flare2", "Waiting out a solar flare"),
                };
                case PresenceKind.Eclipse: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Eclipse1", "Experiencing an eclipse"),
                    UI.KmhText.Get("KMH_RP_Eclipse2", "Living through an eclipse"),
                };
                case PresenceKind.PsychicDrone: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Drone1", "Facing a psychic drone"),
                    UI.KmhText.Get("KMH_RP_Drone2", "Enduring a psychic drone"),
                };
                case PresenceKind.HeatWave: return new[]
                {
                    UI.KmhText.Get("KMH_RP_HeatWave1", "Surviving a heat wave"),
                    UI.KmhText.Get("KMH_RP_HeatWave2", "Sweltering in a heat wave"),
                };
                case PresenceKind.ColdSnap: return new[]
                {
                    UI.KmhText.Get("KMH_RP_ColdSnap1", "Surviving a cold snap"),
                    UI.KmhText.Get("KMH_RP_ColdSnap2", "Weathering a cold snap"),
                };
                case PresenceKind.Flashstorm: return new[] { UI.KmhText.Get("KMH_RP_Flashstorm1", "Weathering a flashstorm") };
                case PresenceKind.NoxiousHaze: return new[] { UI.KmhText.Get("KMH_RP_Haze1", "Enduring a noxious haze") };
                case PresenceKind.Hazard: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Hazard1", "Weathering {0}", label),
                    UI.KmhText.Get("KMH_RP_Hazard2", "Enduring {0}", label),
                };

                case PresenceKind.Raid: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Raid1", "Colony under attack!"),
                    UI.KmhText.Get("KMH_RP_Raid2", "Defending the colony"),
                    UI.KmhText.Get("KMH_RP_Raid3", "Fighting off raiders"),
                    UI.KmhText.Get("KMH_RP_Raid4", "Holding the line"),
                };
                case PresenceKind.RaidMechanoids: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Mech1", "Defending against mechanoids"),
                    UI.KmhText.Get("KMH_RP_Mech2", "Mechanoids attacking!"),
                };
                case PresenceKind.RaidInsects: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Insect1", "Fighting insectoids"),
                    UI.KmhText.Get("KMH_RP_Insect2", "Insectoids in the colony!"),
                };
                case PresenceKind.RaidManhunters: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Manhunter1", "Facing a manhunter pack"),
                    UI.KmhText.Get("KMH_RP_Manhunter2", "Fending off manhunters"),
                };
                case PresenceKind.RaidEntities: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Anomaly1", "Defending against anomalies"),
                    UI.KmhText.Get("KMH_RP_Anomaly2", "Entities attacking!"),
                };
                case PresenceKind.Siege: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Siege1", "Surviving a siege"),
                    UI.KmhText.Get("KMH_RP_Siege2", "Under siege"),
                };
                case PresenceKind.DropPods: return new[]
                {
                    UI.KmhText.Get("KMH_RP_DropPods1", "Defending against a drop pod raid"),
                    UI.KmhText.Get("KMH_RP_DropPods2", "Raiders dropping in!"),
                };
                case PresenceKind.AwayFight: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Away1", "Fighting far from home"),
                    UI.KmhText.Get("KMH_RP_Away2", "Under attack in the field"),
                };
                case PresenceKind.Fire: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Fire1", "Fighting a colony fire"),
                    UI.KmhText.Get("KMH_RP_Fire2", "Putting out a fire"),
                };
                case PresenceKind.MedicalEmergency: return new[]
                {
                    UI.KmhText.Get("KMH_RP_Medical1", "Colony medical emergency"),
                    UI.KmhText.Get("KMH_RP_Medical2", "Struggling to survive"),
                };
                case PresenceKind.PrisonBreak: return new[] { UI.KmhText.Get("KMH_RP_Prison1", "Stopping a prison break") };
                case PresenceKind.SlaveRebellion: return new[] { UI.KmhText.Get("KMH_RP_Rebellion1", "Putting down a slave rebellion") };
                default: return new string[0];
            }
        }

        internal static string[] States(PresenceKind k)
        {
            switch (k)
            {
                case PresenceKind.Colony: return new[]
                {
                    UI.KmhText.Get("KMH_RP_ColonyS1", "Surviving on the Rim"),
                    UI.KmhText.Get("KMH_RP_ColonyS2", "Building a home on the Rim"),
                    UI.KmhText.Get("KMH_RP_ColonyS3", "Day by day on the Rim"),
                    UI.KmhText.Get("KMH_RP_ColonyS4", "Carving out a life on the Rim"),
                };
                case PresenceKind.NewColony: return new[]
                {
                    UI.KmhText.Get("KMH_RP_NewColonyS1", "The first days on the Rim"),
                    UI.KmhText.Get("KMH_RP_NewColonyS2", "Starting from scratch"),
                    UI.KmhText.Get("KMH_RP_NewColonyS3", "Laying the foundations"),
                };
                case PresenceKind.GrowingColony: return new[]
                {
                    UI.KmhText.Get("KMH_RP_GrowingS1", "A colony on the rise"),
                    UI.KmhText.Get("KMH_RP_GrowingS2", "Many mouths to feed"),
                };
                case PresenceKind.GrowingSeason: return new[]
                {
                    UI.KmhText.Get("KMH_RP_SeasonS1", "Planting and harvesting"),
                    UI.KmhText.Get("KMH_RP_SeasonS2", "Bringing in the harvest"),
                };
                case PresenceKind.PrepareWinter: return new[]
                {
                    UI.KmhText.Get("KMH_RP_PrepWinterS1", "Autumn on the Rim"),
                    UI.KmhText.Get("KMH_RP_PrepWinterS2", "Cold days ahead"),
                };
                case PresenceKind.WinterLife: return new[]
                {
                    UI.KmhText.Get("KMH_RP_WinterS1", "Winter on the Rim"),
                    UI.KmhText.Get("KMH_RP_WinterS2", "Keeping warm"),
                };
                case PresenceKind.Research: return new[]
                {
                    UI.KmhText.Get("KMH_RP_ResearchS1", "Pushing the colony forward"),
                    UI.KmhText.Get("KMH_RP_ResearchS2", "One breakthrough at a time"),
                };
                case PresenceKind.Mechanoids: return new[] { UI.KmhText.Get("KMH_RP_MechsS1", "Machines at work") };
                case PresenceKind.Entities: return new[] { UI.KmhText.Get("KMH_RP_EntitiesS1", "Keeping the darkness contained") };
                case PresenceKind.Nomads: return new[] { UI.KmhText.Get("KMH_RP_NomadsS1", "Always on the move") };
                case PresenceKind.Orbit: return new[] { UI.KmhText.Get("KMH_RP_OrbitS1", "Far above the Rim") };
                case PresenceKind.LastingHazard: return new[] { UI.KmhText.Get("KMH_RP_LastingS1", "A hard world to live in") };
                case PresenceKind.WorldMap: return new[] { UI.KmhText.Get("KMH_RP_WorldS1", "Charting the Rim") };
                case PresenceKind.CaravanTravel: return new[]
                {
                    UI.KmhText.Get("KMH_RP_CaravanS1", "Out on the road"),
                    UI.KmhText.Get("KMH_RP_CaravanS2", "Crossing the Rim"),
                };
                case PresenceKind.CaravanVisit: return new[] { UI.KmhText.Get("KMH_RP_VisitS1", "Paying a visit") };
                case PresenceKind.Trading: return new[] { UI.KmhText.Get("KMH_RP_TradeS1", "Buying and selling") };
                case PresenceKind.FormingCaravan: return new[] { UI.KmhText.Get("KMH_RP_FormingS1", "Getting ready to set out") };
                case PresenceKind.TransportPods: return new[] { UI.KmhText.Get("KMH_RP_PodsS1", "Getting ready to launch") };
                case PresenceKind.Expedition:
                case PresenceKind.QuestSite:
                case PresenceKind.AncientRuins:
                case PresenceKind.AttackBase:
                    return new[] { UI.KmhText.Get("KMH_RP_FarFromHome", "Far from home") };
                case PresenceKind.Gravship: return new[] { UI.KmhText.Get("KMH_RP_GravshipS1", "Between landings") };
                case PresenceKind.Visitors: return new[] { UI.KmhText.Get("KMH_RP_VisitorsS1", "Company at the colony") };
                case PresenceKind.TraderVisit: return new[] { UI.KmhText.Get("KMH_RP_TradersS1", "Business at the gates") };
                case PresenceKind.Ritual: return new[] { UI.KmhText.Get("KMH_RP_RitualS1", "The colony comes together") };
                case PresenceKind.RoyalCeremony: return new[] { UI.KmhText.Get("KMH_RP_RoyalS1", "An honor bestowed") };
                case PresenceKind.ResearchDone: return new[] { UI.KmhText.Get("KMH_RP_ResearchDoneS1", "New technology unlocked") };
                case PresenceKind.NewSettlement: return new[] { UI.KmhText.Get("KMH_RP_SettledS1", "The colony grows") };
                case PresenceKind.ExtremeHeat:
                case PresenceKind.HeatWave:
                    return new[] { UI.KmhText.Get("KMH_RP_KeepCool", "Keeping cool") };
                case PresenceKind.ExtremeCold:
                case PresenceKind.ColdSnap:
                    return new[] { UI.KmhText.Get("KMH_RP_BundleUp", "Bundling up") };
                case PresenceKind.HarshWinter: return new[] { UI.KmhText.Get("KMH_RP_HarshWinterS1", "Bitter cold outside") };
                case PresenceKind.ServerEvent: return new[] { UI.KmhText.Get("KMH_RP_ServerEventS1", "A server-wide event is on") };
                case PresenceKind.RaidApproaching: return new[]
                {
                    UI.KmhText.Get("KMH_RP_ApproachS1", "Preparing the defenses"),
                    UI.KmhText.Get("KMH_RP_ApproachS2", "Manning the walls"),
                };
                case PresenceKind.MechCluster: return new[] { UI.KmhText.Get("KMH_RP_ClusterS1", "Planning the assault") };
                case PresenceKind.Recovering: return new[]
                {
                    UI.KmhText.Get("KMH_RP_RecoverS1", "Tending the wounded"),
                    UI.KmhText.Get("KMH_RP_RecoverS2", "Counting the cost"),
                };
                case PresenceKind.InjuredColonists: return new[] { UI.KmhText.Get("KMH_RP_InjuredS1", "Doctors at work") };
                case PresenceKind.Disease: return new[] { UI.KmhText.Get("KMH_RP_DiseaseS1", "Sickness in the colony") };
                case PresenceKind.FoodShortage: return new[] { UI.KmhText.Get("KMH_RP_FoodS1", "Rationing supplies") };
                case PresenceKind.ShipLaunch: return new[] { UI.KmhText.Get("KMH_RP_ShipS1", "Leaving the Rim behind") };
                case PresenceKind.ToxicFallout: return new[] { UI.KmhText.Get("KMH_RP_ToxicS1", "Staying indoors") };
                case PresenceKind.VolcanicWinter: return new[] { UI.KmhText.Get("KMH_RP_VolcanicS1", "Dark skies overhead") };
                case PresenceKind.SolarFlare: return new[] { UI.KmhText.Get("KMH_RP_FlareS1", "The power is down") };
                case PresenceKind.Eclipse: return new[] { UI.KmhText.Get("KMH_RP_EclipseS1", "Darkness at midday") };
                case PresenceKind.PsychicDrone: return new[] { UI.KmhText.Get("KMH_RP_DroneS1", "Nerves on edge") };
                case PresenceKind.Flashstorm: return new[] { UI.KmhText.Get("KMH_RP_FlashstormS1", "Lightning on the horizon") };
                case PresenceKind.NoxiousHaze: return new[] { UI.KmhText.Get("KMH_RP_HazeS1", "Pollution in the air") };
                case PresenceKind.Hazard: return new[] { UI.KmhText.Get("KMH_RP_HazardS1", "Waiting for it to pass") };
                case PresenceKind.Raid: return new[]
                {
                    UI.KmhText.Get("KMH_RP_RaidS1", "Defending against raiders"),
                    UI.KmhText.Get("KMH_RP_RaidS2", "Under enemy attack"),
                    UI.KmhText.Get("KMH_RP_RaidS3", "Protecting the settlement"),
                    UI.KmhText.Get("KMH_RP_Survival", "Fighting for survival"),
                };
                case PresenceKind.RaidMechanoids: return new[]
                {
                    UI.KmhText.Get("KMH_RP_MechRaidS1", "Holding off the machines"),
                    UI.KmhText.Get("KMH_RP_Survival", "Fighting for survival"),
                };
                case PresenceKind.RaidInsects: return new[]
                {
                    UI.KmhText.Get("KMH_RP_InsectS1", "Clearing out the hive"),
                    UI.KmhText.Get("KMH_RP_Survival", "Fighting for survival"),
                };
                case PresenceKind.RaidManhunters: return new[]
                {
                    UI.KmhText.Get("KMH_RP_ManhunterS1", "Animals on the rampage"),
                    UI.KmhText.Get("KMH_RP_ManhunterS2", "Staying behind the walls"),
                };
                case PresenceKind.RaidEntities: return new[]
                {
                    UI.KmhText.Get("KMH_RP_AnomalyS1", "Holding back the dark"),
                    UI.KmhText.Get("KMH_RP_Survival", "Fighting for survival"),
                };
                case PresenceKind.Siege: return new[]
                {
                    UI.KmhText.Get("KMH_RP_SiegeS1", "Shells falling"),
                    UI.KmhText.Get("KMH_RP_SiegeS2", "Breaking the siege"),
                };
                case PresenceKind.DropPods: return new[]
                {
                    UI.KmhText.Get("KMH_RP_DropPodsS1", "Enemies from the sky"),
                    UI.KmhText.Get("KMH_RP_DropPodsS2", "Fighting inside the walls"),
                };
                case PresenceKind.AwayFight: return new[] { UI.KmhText.Get("KMH_RP_AwayS1", "Holding out") };
                case PresenceKind.Fire: return new[] { UI.KmhText.Get("KMH_RP_FireS1", "All hands on the fire line") };
                case PresenceKind.MedicalEmergency: return new[] { UI.KmhText.Get("KMH_RP_MedicalS1", "Too many wounded") };
                case PresenceKind.PrisonBreak: return new[] { UI.KmhText.Get("KMH_RP_PrisonS1", "Prisoners on the loose") };
                case PresenceKind.SlaveRebellion: return new[] { UI.KmhText.Get("KMH_RP_RebellionS1", "Restoring order") };
                default: return new string[0];
            }
        }

        // Named by the event's type, never its server-written title, so the line stays fixed text.
        internal static string ServerEventLine(string type)
        {
            switch (type)
            {
                case World.Dto.WorldEventDto.TaxHoliday:       return UI.KmhText.Get("KMH_RP_EventTaxHoliday", "Enjoying a tax holiday");
                case World.Dto.WorldEventDto.MarketBoom:       return UI.KmhText.Get("KMH_RP_EventMarketBoom", "Trading in a market boom");
                case World.Dto.WorldEventDto.MarketCrash:      return UI.KmhText.Get("KMH_RP_EventMarketCrash", "Riding out a market crash");
                case World.Dto.WorldEventDto.ResourceShortage: return UI.KmhText.Get("KMH_RP_EventShortage", "Weathering a resource shortage");
                case World.Dto.WorldEventDto.DoubleWorkerXp:   return UI.KmhText.Get("KMH_RP_EventDoubleXp", "Working through double worker XP");
                case World.Dto.WorldEventDto.HouseStipend:     return UI.KmhText.Get("KMH_RP_EventStipend", "Collecting the house stipend");
                case World.Dto.WorldEventDto.BountyTarget:     return UI.KmhText.Get("KMH_RP_EventBounty", "Chasing a server bounty");
                default:                                       return UI.KmhText.Get("KMH_RP_ServerEvent1", "Taking part in a server event");
            }
        }

        internal static string AlliesLine() => UI.KmhText.Get("KMH_RP_Allies", "Fighting alongside allies");

        // Random start per player, then a steady walk: players differ, and nobody sees the words churn.
        internal static string Pick(string[] lines, int seed, DateTime sinceUtc, DateTime nowUtc, TimeSpan every)
        {
            if (lines == null || lines.Length == 0) return "";
            long step = every.Ticks <= 0 || nowUtc <= sinceUtc ? 0 : (nowUtc - sinceUtc).Ticks / every.Ticks;
            int start = (seed & int.MaxValue) % lines.Length;
            return lines[(int)((start + step) % lines.Length)];
        }

        internal static TimeSpan VariantEvery(PresenceTier tier)
            => tier == PresenceTier.Normal ? BaselineVariantEvery : EventVariantEvery;
    }
}
