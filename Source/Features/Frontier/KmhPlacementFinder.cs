using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KMHPatch.Diagnostics;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Frontier
{
    // Deterministic and RNG-free: agreement between independent clients is the only evidence the server has.
    internal static class KmhPlacementFinder
    {
        // Bounded so a huge planet cannot stall the game; giving up simply sends no proposal.
        private const int MaxProbes = 2000;

        public static bool TryFindCandidate(long nonce, ICollection<int> exclude, out int tile, out string why)
        {
            tile = -1; why = null;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) { why = "no world"; return false; }

            int count = grid.TilesCount;
            if (count <= 0) { why = "empty world"; return false; }

            int start = (int)((ulong)((ulong)nonce * 2654435761UL) % (ulong)count);
            var reason = new StringBuilder();
            int probes = count < MaxProbes ? count : MaxProbes;

            for (int i = 0; i < probes; i++)
            {
                int candidate = (start + i) % count;
                if (exclude != null && exclude.Contains(candidate)) continue;
                try
                {
                    var pt = new PlanetTile(candidate, 0);
                    if (!pt.Valid) continue;
                    if (Find.WorldObjects != null && Find.WorldObjects.AnyWorldObjectAt(pt)) continue;
                    reason.Clear();
                    if (!TileFinder.IsValidTileForNewSettlement(pt, reason, false)) continue;
                }
                catch { continue; }
                tile = candidate;
                return true;
            }
            why = $"no suitable tile within {probes} probes";
            return false;
        }

        // FNV-1a, not string.GetHashCode, which is randomised per process and would differ between clients on one world.
        public static string WorldFingerprint()
        {
            WorldInfo info = Find.World?.info;
            if (info == null) return "";
            return FingerprintOf(info.seedString, info.planetCoverage, info.persistentRandomValue,
                                 Find.WorldGrid?.TilesCount ?? 0);
        }

        // Invariant throughout, or a comma-decimal locale writes 0,3000 and stops corroborating the same planet.
        internal static string FingerprintOf(string seed, float coverage, int randomValue, int tiles)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return Fnv1a((seed ?? "") + "|" + coverage.ToString("0.0000", inv)
                         + "|" + randomValue.ToString(inv) + "|" + tiles.ToString(inv));
        }

        private static string Fnv1a(string s)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in s)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        internal static void LogGaveUp(string why) => KmhLog.Debug("Frontier: no placement candidate - " + why);
    }
}
