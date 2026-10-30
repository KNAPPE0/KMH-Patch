using System;

namespace KMHPatch.Features.Discord
{
    internal enum PresenceTier { Normal = 0, Medium = 1, High = 2, Critical = 3 }

    // One art asset each on the Discord application.
    internal enum PresenceIcon { None, Combat, Hazard, Emergency, Caravan, Colony, Research, Multiplayer }

    // Closed list of reviewed fixed text; only a condition label and an extension claim carry outside words.
    internal enum PresenceKind
    {
        None,
        MainMenu, Connecting, Reconnecting, PreparingMods, Loading, Disconnected,
        Colony, NewColony, GrowingColony, GrowingSeason, PrepareWinter, WinterLife, Research, Mechanoids, Entities,
        Nomads, Orbit, LastingHazard,
        WorldMap, CaravanTravel, CaravanVisit, Trading, FormingCaravan, TransportPods, Expedition, QuestSite,
        AncientRuins, Gravship, Visitors, TraderVisit, Ritual, RoyalCeremony, ResearchDone, NewSettlement,
        ExtremeHeat, ExtremeCold, HarshWinter, ServerScreen, ServerEvent, Extension,
        RaidApproaching, MechCluster, Recovering, InjuredColonists, Disease, FoodShortage, ShipLaunch, AttackBase,
        ToxicFallout, VolcanicWinter, SolarFlare, Eclipse, PsychicDrone, HeatWave, ColdSnap, Flashstorm, NoxiousHaze,
        Hazard,
        Raid, RaidMechanoids, RaidInsects, RaidManhunters, RaidEntities, Siege, DropPods, AwayFight, Fire,
        MedicalEmergency, PrisonBreak, SlaveRebellion,
    }

    internal struct PresenceCandidate
    {
        public PresenceKind Kind;
        public PresenceTier Tier;
        public string Label;
        public string Sub;
        public bool WithAllies;

        public PresenceCandidate(PresenceKind kind, PresenceTier tier, string label = null, string sub = null)
        {
            Kind = kind; Tier = tier; Label = label; Sub = sub; WithAllies = false;
        }
    }

    internal sealed class PresenceChoice
    {
        public PresenceKind Kind = PresenceKind.None;
        public PresenceTier Tier;
        public string Label;
        public string Sub;
        public bool WithAllies;
        public DateTime SinceUtc;   // the catalog keys its variant off this, so the words hold still
        public bool Urgent;         // set only on the sample that switched into Critical

        public bool SameAs(PresenceChoice o)
            => o != null && o.Kind == Kind && o.Label == Label && o.Sub == Sub && o.WithAllies == WithAllies;
    }

    internal static class PresenceKinds
    {
        public static bool IsRaid(PresenceKind k)
            => k == PresenceKind.Raid || k == PresenceKind.RaidMechanoids || k == PresenceKind.RaidInsects
            || k == PresenceKind.RaidManhunters || k == PresenceKind.RaidEntities || k == PresenceKind.Siege
            || k == PresenceKind.DropPods;

        public static bool IsPhase(PresenceKind k) => k >= PresenceKind.MainMenu && k <= PresenceKind.Disconnected;
    }
}
