using System;
using System.Collections.Generic;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.Features.PlayerStats;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.Features.Reputation;

namespace KMHPatch.Features.Standings
{
    internal sealed class GuildAgg
    {
        public string Name = "";
        public int    Members;
        public long   Treasury;
        public long   Wealth;
        public long   Kills;
        public long   SiteSilver;
        public long   SalesEarned;
        public long   PurchasesSpent;
        public int    Contracts;
        public int    Sites;
        public int    RepSum;
        public int    RepCount;

        public long TradeVolume => SalesEarned + PurchasesSpent;
        public int  RepAvg      => RepCount > 0 ? RepSum / RepCount : 0;
    }

    internal static class StandingsData
    {
        // Simulated playtest accounts rank only when the server allows it, like the published boards.
        public static List<PlayerLeaderboardEntry> Players()
        {
            List<PlayerLeaderboardEntry> all = PlayerStatsCache.Entries ?? new List<PlayerLeaderboardEntry>();
            if (Identity.KmhStaff.SimulatedRanked) return all;
            return all.FindAll(e => e != null && e.StaffRole != "sim");
        }

        public static List<GuildAgg> GuildAggs()
        {
            Dictionary<string, GuildAgg> map = new Dictionary<string, GuildAgg>(StringComparer.OrdinalIgnoreCase);
            foreach (PlayerLeaderboardEntry e in Players())
            {
                if (string.IsNullOrEmpty(e.GuildName)) continue;
                if (!map.TryGetValue(e.GuildName, out GuildAgg g)) { g = new GuildAgg { Name = e.GuildName }; map[e.GuildName] = g; }
                // Map wealth only: each member's KMH figure already holds a share of the vault added here as Treasury.
                g.Wealth         += e.Wealth;
                g.Kills          += e.Kills;
                g.SiteSilver     += e.SiteSilverProduced;
                g.SalesEarned    += e.SalesEarned;
                g.PurchasesSpent += e.PurchasesSpent;
                g.Contracts      += e.QuestsCompleted;
                g.Sites          += e.SitesOwned;
                int rep = ReputationCache.ScoreFor(e.Username);
                g.RepSum += rep; g.RepCount++;
            }

            // From the guild snapshot, so guilds with no reporting members still appear.
            GuildLeaderboardSnapshot gl = GuildLeaderboardCache.HasSnapshot ? GuildLeaderboardCache.Snapshot : null;
            if (gl?.Guilds != null)
                foreach (GuildLeaderboardEntry ge in gl.Guilds)
                {
                    if (string.IsNullOrEmpty(ge.Name)) continue;
                    if (!map.TryGetValue(ge.Name, out GuildAgg g)) { g = new GuildAgg { Name = ge.Name }; map[ge.Name] = g; }
                    g.Members  = ge.MemberCount;
                    g.Treasury = ge.TreasurySilver;
                }

            return new List<GuildAgg>(map.Values);
        }

        // Both halves are measured by the server, so this is comparable across players; -1 means nothing to score yet.
        public static int Focus(long activeSeconds, long connectedSeconds)
        {
            if (connectedSeconds <= 0 || activeSeconds <= 0) return -1;
            long pct = activeSeconds * 100L / connectedSeconds;
            return pct > 100L ? 100 : (int)pct;
        }

        public static int  Rep(string username)     => ReputationCache.ScoreFor(username);
        public static string RepTier(string username) => ReputationCache.TierFor(username);
    }
}
