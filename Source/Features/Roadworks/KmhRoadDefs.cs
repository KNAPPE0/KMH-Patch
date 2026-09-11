using System.Collections.Generic;
using KMHPatch.Diagnostics;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Roadworks
{
    // The only place a KMH tier meets a RimWorld RoadDef. Never persisted, never sent over the wire.
    internal static class KmhRoadDefs
    {
        public const string TierTrail   = "trail";
        public const string TierRoad    = "road";
        public const string TierHighway = "highway";

        // Ancient defs are omitted (they read as ruins), and vanilla roads share movementCostMultiplier, so the ladder buys look and draw priority, not speed.
        private static readonly Dictionary<string, string[]> Candidates = new Dictionary<string, string[]>
        {
            { TierTrail,   new[] { "DirtPath", "DirtRoad", "StoneRoad" } },
            { TierRoad,    new[] { "DirtRoad", "StoneRoad", "DirtPath" } },
            { TierHighway, new[] { "StoneRoad", "DirtRoad", "DirtPath" } },
        };

        private static readonly Dictionary<string, RoadDef> _resolved = new Dictionary<string, RoadDef>();
        private static readonly HashSet<string> _warned = new HashSet<string>();

        public static string Normalize(string tier)
        {
            if (string.IsNullOrEmpty(tier)) return TierTrail;
            string t = tier.Trim().ToLowerInvariant();
            return Candidates.ContainsKey(t) ? t : TierTrail;
        }

        // Null when the game has no usable road def; callers must treat that as "cannot apply".
        public static RoadDef ForTier(string tier)
        {
            string t = Normalize(tier);
            if (_resolved.TryGetValue(t, out RoadDef cached)) return cached;

            RoadDef found = null;
            foreach (string defName in Candidates[t])
            {
                found = DefDatabase<RoadDef>.GetNamedSilentFail(defName);
                if (found == null) continue;
                if (defName != Candidates[t][0] && _warned.Add(t))
                    KmhLog.Warn($"Roadworks: this game has no '{Candidates[t][0]}' road, so {t} roads will be built as '{defName}'.");
                break;
            }
            if (found == null && _warned.Add(t))
                KmhLog.Warn($"Roadworks: no usable RoadDef for tier '{t}' - roads of that tier cannot be drawn on this game.");

            _resolved[t] = found;
            return found;
        }

        public static int RankOf(string tier)
        {
            switch (Normalize(tier))
            {
                case TierHighway: return 3;
                case TierRoad:    return 2;
                default:          return 1;
            }
        }

        // Mirrored byte-for-byte on the server, which enforces it; the client only greys out what this refuses.
        public static bool TierRankAllowed(int tierRank, int siteTier)
        {
            int max = siteTier >= 3 ? 3 : (siteTier <= 1 ? 1 : 2);
            return tierRank >= 1 && tierRank <= max;
        }

        public static bool AllowedAtSiteTier(string tier, int siteTier) => TierRankAllowed(RankOf(tier), siteTier);

        // Read off the resolved def rather than a second hard-coded ladder.
        public static int PriorityOf(string tier)
        {
            RoadDef d = ForTier(tier);
            return d?.priority ?? 0;
        }

        internal static void ResetCacheForTesting()
        {
            _resolved.Clear();
            _warned.Clear();
        }
    }
}
