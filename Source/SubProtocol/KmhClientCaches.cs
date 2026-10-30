using System;

namespace KMHPatch.SubProtocol
{
    // Wipe every feature cache on disconnect/server-switch so a new server never briefly shows the old server's data.
    internal static class KmhClientCaches
    {
        // Each clear on its own: one shared try/catch let a single throw skip every cache below it.
        private static void SafeClear(string name, Action clear)
        {
            try { clear(); }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"Cache clear on disconnect failed for {name}: {ex.Message}"); }
        }

        public static void ClearAll()
        {
            SafeClear("theme", () => UI.KmhTheme.ClearServerTheme());
            SafeClear("marker colours", () => Features.Sites.KmhMarkerColors.ClearServerColors());
            SafeClear("staff", () => Features.Identity.KmhStaff.Clear());
            SafeClear("comms revision", () => KmhHandshakeHandler.ResetCommsRevision());
            SafeClear("fragments", () => KmhFragments.Clear());
            SafeClear("frame-step faults", () => Diagnostics.KmhFrameSteps.Clear());   // a fault on that server is worth hearing about on this one
            SafeClear("treasury", () => Features.Treasury.TreasuryCache.Clear());
            // In-flight deposit approvals only. The durable txn ledger is recovery state and is NOT touched here.
            SafeClear("deposit requests", () => Features.Treasury.TreasuryHandler.ClearInFlight());
            SafeClear("marketplace", () => Features.Marketplace.MarketplaceCache.Clear());
            SafeClear("auctions", () => Features.Auctions.AuctionCache.Clear());
            SafeClear("wants", () => Features.WantBoard.WantCache.Clear());
            SafeClear("mail", () => Features.Mail.MailCache.Clear());
            // Pending federated operations belong to the server just left; showing them on another would be a lie.
            SafeClear("connected treasury", () => Features.ConnectedTreasury.ConnectedTreasuryCache.Clear());
            SafeClear("connected guild", () => Features.ConnectedGuild.ConnectedGuildCache.Clear());
            SafeClear("federation", () => Features.Federation.FederationCache.Clear());
            SafeClear("join ticket", () => Features.Global.KmhJoinTicketClient.ForgetSession());   // verified there is not verified here
            SafeClear("chat", () => Features.Chat.ChatCache.Clear());
            SafeClear("chat images", () => Features.Chat.ChatImageCache.Clear());
            SafeClear("chat media", () => Features.Chat.ChatMediaClient.Clear());
            SafeClear("media refresh", () => Features.Chat.ChatMediaRefreshClient.Clear());
            SafeClear("video player", () => Features.Chat.ChatVideoPlayer.Clear());
            SafeClear("watch links", () => Features.Chat.ChatYouTube.Clear());
            SafeClear("video server", () => Features.Chat.ChatVideoServer.Clear());
            SafeClear("chat moderation", () => Features.Chat.ChatModerationCache.Clear());
            SafeClear("chat roster", () => Features.Chat.ChatRosterCache.Clear());
            SafeClear("sites", () => Features.Sites.SiteCache.Clear());
            SafeClear("site catalog", () => Features.Sites.SiteCatalogCache.Clear());
            SafeClear("site build quote", () => Features.Sites.SiteQuoteCache.Clear());
            SafeClear("roadworks", () => Features.Roadworks.RoadworksCache.Clear());
            SafeClear("guild", () => Features.Guilds.GuildCache.Clear());
            SafeClear("guild standings", () => Features.Guilds.GuildLeaderboardCache.Clear());
            SafeClear("leaderboards", () => Features.Leaderboards.LeaderboardCache.Clear());
            SafeClear("quests", () => Features.Quests.QuestCache.Clear());
            SafeClear("world", () => Features.World.WorldCache.Clear());
            SafeClear("reputation", () => Features.Reputation.ReputationCache.Clear());
            SafeClear("player stats", () => Features.PlayerStats.PlayerStatsCache.Clear());
            SafeClear("activity", () => Features.PlayerStats.KmhActivity.Reset());   // uncredited playtime was owed to the server we left
            SafeClear("colonist profiles", () => Features.PlayerStats.ColonistProfileCache.Clear());
            SafeClear("colonist roster", () => Features.PlayerStats.ColonistRosterCache.Clear());
            SafeClear("linked accounts", () => Features.LinkedAccounts.LinkedAccountsCache.Clear());
            SafeClear("season archive", () => Features.Seasons.SeasonArchiveCache.Clear());
            // Owner-only settings, so leaving them up would show one server's configuration while on another.
            SafeClear("server config", () => Features.Admin.ServerConfigCache.Clear());
        }
    }
}
