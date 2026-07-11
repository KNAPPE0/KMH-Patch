namespace KMHPatch.SubProtocol
{
    // Wipe every feature cache on disconnect/server-switch so a new server never briefly shows the old server's data.
    internal static class KmhClientCaches
    {
        public static void ClearAll()
        {
            try
            {
                Features.Treasury.TreasuryCache.Clear();
                Features.Marketplace.MarketplaceCache.Clear();
                Features.Auctions.AuctionCache.Clear();
                Features.WantBoard.WantCache.Clear();
                Features.Sites.SiteCache.Clear();
                Features.Sites.SiteCatalogCache.Clear();
                Features.Guilds.GuildCache.Clear();
                Features.Guilds.GuildLeaderboardCache.Clear();
                Features.Quests.QuestCache.Clear();
                Features.World.WorldCache.Clear();
                Features.Reputation.ReputationCache.Clear();
                Features.PlayerStats.PlayerStatsCache.Clear();
                Features.PlayerStats.ColonistProfileCache.Clear();
                Features.PlayerStats.ColonistRosterCache.Clear();
                Features.LinkedAccounts.LinkedAccountsCache.Clear();
            }
            catch (System.Exception ex) { Diagnostics.KmhLog.Warn($"Cache clear on disconnect threw: {ex.Message}"); }
        }
    }
}
