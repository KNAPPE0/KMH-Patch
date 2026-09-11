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
    internal static class KmhRefresh
    {
        // Every request a join makes, named so a failed send is retried alone instead of stranding that feature on "Loading…".
        internal static readonly (string Name, System.Func<bool> Send)[] All =
        {
            ("sites",      SiteHandler.RequestSnapshot),
            ("roadworks",  Features.Roadworks.RoadworksHandler.RequestSnapshot),
            ("guild",      GuildHandler.RequestSnapshot),
            ("world",      WorldHandler.RequestSnapshot),
            ("treasury",   TreasuryHandler.RequestSnapshot),
            ("marketplace",MarketplaceHandler.RequestSnapshot),
            ("auctions",   AuctionHandler.RequestSnapshot),
            ("wants",      WantHandler.RequestSnapshot),
            ("mail",       Features.Mail.MailHandler.RequestSnapshot),
            ("chat",       () => Features.Chat.ChatHandler.RequestSnapshot()),
            ("quests",     QuestHandler.RequestSnapshot),
            ("standings",  PlayerStatsHandler.RequestSnapshot),
            ("reputation", ReputationCache.RequestSnapshot),
            ("accounts",   LinkedAccountsHandler.RequestSnapshot),
            ("enforcement",Features.Enforcement.EnforcementHandler.RequestSnapshot),
        };

        public static void RequestAll() => RequestAll(out _);

        public static void RequestAll(out System.Collections.Generic.List<string> failed)
        {
            failed = new System.Collections.Generic.List<string>();
            if (!KmhDispatcher.IsKmhServer) return;

            foreach ((string name, System.Func<bool> send) in All)
            {
                bool ok = false;
                try { ok = send(); } catch { }
                if (!ok) failed.Add(name);
            }

            if (failed.Count == 0)
                KmhLog.Debug("KMH refresh: requested fresh snapshots for every feature.");
            else
                KmhLog.Warn($"KMH refresh: {string.Join(", ", failed)} did not send - will retry.");
        }
    }
}
