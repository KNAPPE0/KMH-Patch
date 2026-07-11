using KMHPatch.Diagnostics;
using KMHPatch.Features.Auctions;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Marketplace;
using KMHPatch.Features.PlayerStats;
using KMHPatch.Features.Quests;
using KMHPatch.Features.Reputation;
using KMHPatch.Features.Sites;
using KMHPatch.Features.Treasury;
using KMHPatch.Features.WantBoard;
using KMHPatch.Features.World;

namespace KMHPatch.SubProtocol
{
    // Ask the server for a fresh snapshot of every feature at once (e.g. when the KMH tab opens) so the dashboard +
    // caches are current without waiting for the player to open each dialog. Each send is guarded; server pushes fill
    // the caches. No-op off a KMH server.
    internal static class KmhRefresh
    {
        public static void RequestAll()
        {
            if (!KmhDispatcher.IsKmhServer) return;
            void Try(System.Func<bool> send) { try { send(); } catch { } }

            Try(TreasuryHandler.RequestSnapshot);
            Try(GuildHandler.RequestSnapshot);
            Try(MarketplaceHandler.RequestSnapshot);
            Try(AuctionHandler.RequestSnapshot);
            Try(WantHandler.RequestSnapshot);
            Try(QuestHandler.RequestSnapshot);
            Try(SiteHandler.RequestSnapshot);
            Try(WorldHandler.RequestSnapshot);
            Try(PlayerStatsHandler.RequestSnapshot);
            Try(ReputationCache.RequestSnapshot);
            Try(LinkedAccountsHandler.RequestSnapshot);
            try { Features.Enforcement.EnforcementHandler.RequestSnapshot(); } catch { }   // void return, not Func<bool>
            KmhLog.Debug("KMH refresh: requested fresh snapshots for treasury/guild/marketplace/auction/want/quest/site/world/standings/reputation/linked/enforcement.");
        }
    }
}
