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
            SafeClear("theme", () => UI.KmhTheme.ClearServerTheme());   // the next server's defaults must not inherit this one's
            SafeClear("marker colours", () => Features.Sites.KmhMarkerColors.ClearServerColors());
            SafeClear("staff", () => Features.Identity.KmhStaff.Clear());   // and neither do its badges or role map
            SafeClear("comms revision", () => KmhHandshakeHandler.ResetCommsRevision());   // a new server starts its own sequence
            SafeClear("fragments", () => KmhFragments.Clear());   // half-assembled transfers belong to the server we left
            SafeClear("frame-step faults", () => Diagnostics.KmhFrameSteps.Clear());   // a fault on that server is worth hearing about on this one
            SafeClear("treasury", () => Features.Treasury.TreasuryCache.Clear());
            // In-flight deposit approvals only. The durable txn ledger is recovery state and is NOT touched here.
            SafeClear("deposit requests", () => Features.Treasury.TreasuryHandler.ClearInFlight());
            SafeClear("marketplace", () => Features.Marketplace.MarketplaceCache.Clear());
            SafeClear("auctions", () => Features.Auctions.AuctionCache.Clear());
            SafeClear("wants", () => Features.WantBoard.WantCache.Clear());
            SafeClear("mail", () => Features.Mail.MailCache.Clear());
            SafeClear("chat", () => Features.Chat.ChatCache.Clear());
            SafeClear("chat images", () => Features.Chat.ChatImageCache.Clear());   // and the textures those messages pulled in
            SafeClear("chat media", () => Features.Chat.ChatMediaClient.Clear());  // anything the previous server converted for us
            SafeClear("media refresh", () => Features.Chat.ChatMediaRefreshClient.Clear());   // refreshed links belong to that server
            SafeClear("video player", () => Features.Chat.ChatVideoPlayer.Clear());  // and any video still streaming from it
            SafeClear("watch links", () => Features.Chat.ChatYouTube.Clear());      // resolved links expire, and the policy was theirs
            SafeClear("video server", () => Features.Chat.ChatVideoServer.Clear());  // its video port belongs to that server too
            SafeClear("chat moderation", () => Features.Chat.ChatModerationCache.Clear());
            SafeClear("chat roster", () => Features.Chat.ChatRosterCache.Clear());  // who is online belongs to the server that said so
            SafeClear("sites", () => Features.Sites.SiteCache.Clear());
            SafeClear("site catalog", () => Features.Sites.SiteCatalogCache.Clear());
            SafeClear("site build quote", () => Features.Sites.SiteQuoteCache.Clear());
            SafeClear("roadworks", () => Features.Roadworks.RoadworksCache.Clear());
            SafeClear("guild", () => Features.Guilds.GuildCache.Clear());
            SafeClear("guild standings", () => Features.Guilds.GuildLeaderboardCache.Clear());
            SafeClear("quests", () => Features.Quests.QuestCache.Clear());
            SafeClear("world", () => Features.World.WorldCache.Clear());
            SafeClear("reputation", () => Features.Reputation.ReputationCache.Clear());
            SafeClear("player stats", () => Features.PlayerStats.PlayerStatsCache.Clear());
            SafeClear("activity", () => Features.PlayerStats.KmhActivity.Reset());   // uncredited playtime was owed to the server we left
            SafeClear("colonist profiles", () => Features.PlayerStats.ColonistProfileCache.Clear());
            SafeClear("colonist roster", () => Features.PlayerStats.ColonistRosterCache.Clear());
            SafeClear("linked accounts", () => Features.LinkedAccounts.LinkedAccountsCache.Clear());
            SafeClear("season archive", () => Features.Seasons.SeasonArchiveCache.Clear());
        }
    }
}
