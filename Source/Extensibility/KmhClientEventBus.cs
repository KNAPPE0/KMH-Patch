using System;
using KMH.Sdk.Client.Apis;
using KMH.Sdk.Client.Events;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Auctions;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Marketplace;
using KMHPatch.Features.PlayerStats;
using KMHPatch.Features.Quests;
using KMHPatch.Features.Reputation;
using KMHPatch.Features.Seasons;
using KMHPatch.Features.Sites;
using KMHPatch.Features.Treasury;
using KMHPatch.Features.WantBoard;
using KMHPatch.Features.World;

namespace KMHPatch.Extensibility
{
    // Dispatch walks the invocation list itself, so one throwing subscriber cannot stop the rest.
    internal sealed class KmhClientEventBus : IKmhClientEvents
    {
        public static KmhClientEventBus Instance { get; } = new KmhClientEventBus();

        private KmhClientEventBus()
        {
            // In the singleton ctor so it happens once; a second subscription doubles every extension callback.
            TreasuryCache.Updated         += () => SafeRaise(TreasuryCacheUpdated,         new TreasuryCacheUpdatedEvent(),         nameof(TreasuryCacheUpdated));
            MarketplaceCache.Updated      += () => SafeRaise(MarketplaceCacheUpdated,      new MarketplaceCacheUpdatedEvent(),      nameof(MarketplaceCacheUpdated));
            QuestCache.Updated            += () => SafeRaise(QuestCacheUpdated,            new QuestCacheUpdatedEvent(),            nameof(QuestCacheUpdated));
            GuildCache.Updated            += () => SafeRaise(GuildCacheUpdated,            new GuildCacheUpdatedEvent(),            nameof(GuildCacheUpdated));
            GuildLeaderboardCache.Updated += () => SafeRaise(GuildLeaderboardCacheUpdated, new GuildLeaderboardCacheUpdatedEvent(), nameof(GuildLeaderboardCacheUpdated));
            PlayerStatsCache.Updated      += () => SafeRaise(PlayerStatsCacheUpdated,      new PlayerStatsCacheUpdatedEvent(),      nameof(PlayerStatsCacheUpdated));
            LinkedAccountsCache.Updated   += () => SafeRaise(LinkedAccountsCacheUpdated,   new LinkedAccountsCacheUpdatedEvent(),   nameof(LinkedAccountsCacheUpdated));
            AuctionCache.Updated          += () => SafeRaise(AuctionCacheUpdated,          new AuctionCacheUpdatedEvent(),          nameof(AuctionCacheUpdated));
            WorldCache.Updated            += () => SafeRaise(WorldCacheUpdated,            new WorldCacheUpdatedEvent(),            nameof(WorldCacheUpdated));
            WantCache.Updated             += () => SafeRaise(WantCacheUpdated,             new WantCacheUpdatedEvent(),             nameof(WantCacheUpdated));
            SiteCache.Updated             += () => SafeRaise(SiteCacheUpdated,             new SiteCacheUpdatedEvent(),             nameof(SiteCacheUpdated));
            Features.Roadworks.RoadworksCache.Updated += () => SafeRaise(RoadNetworkCacheUpdated, new RoadNetworkCacheUpdatedEvent(), nameof(RoadNetworkCacheUpdated));
            ReputationCache.Updated       += () => SafeRaise(ReputationCacheUpdated,       new ReputationCacheUpdatedEvent(),       nameof(ReputationCacheUpdated));
            SeasonArchiveCache.Updated    += () => SafeRaise(SeasonArchiveCacheUpdated,    new SeasonArchiveCacheUpdatedEvent(),    nameof(SeasonArchiveCacheUpdated));
            Features.Chat.ChatCache.Updated           += () => SafeRaise(ChatCacheUpdated,           new ChatCacheUpdatedEvent(),           nameof(ChatCacheUpdated));
            Features.Chat.ChatModerationCache.Updated += () => SafeRaise(ChatModerationCacheUpdated, new ChatModerationCacheUpdatedEvent(), nameof(ChatModerationCacheUpdated));
            Features.Mail.MailCache.Updated           += () => SafeRaise(MailCacheUpdated,           new MailCacheUpdatedEvent(),           nameof(MailCacheUpdated));
        }

        public event Action<KmhServerConnectedEvent>             KmhServerConnected;
        public event Action<KmhServerDisconnectedEvent>          KmhServerDisconnected;
        public event Action<TreasuryCacheUpdatedEvent>           TreasuryCacheUpdated;
        public event Action<MarketplaceCacheUpdatedEvent>        MarketplaceCacheUpdated;
        public event Action<QuestCacheUpdatedEvent>              QuestCacheUpdated;
        public event Action<GuildCacheUpdatedEvent>              GuildCacheUpdated;
        public event Action<GuildLeaderboardCacheUpdatedEvent>   GuildLeaderboardCacheUpdated;
        public event Action<PlayerStatsCacheUpdatedEvent>        PlayerStatsCacheUpdated;
        public event Action<LinkedAccountsCacheUpdatedEvent>     LinkedAccountsCacheUpdated;
        public event Action<AuctionCacheUpdatedEvent>           AuctionCacheUpdated;
        public event Action<WorldCacheUpdatedEvent>             WorldCacheUpdated;
        public event Action<WantCacheUpdatedEvent>              WantCacheUpdated;
        public event Action<SiteCacheUpdatedEvent>              SiteCacheUpdated;
        public event Action<RoadNetworkCacheUpdatedEvent>      RoadNetworkCacheUpdated;
        public event Action<ReputationCacheUpdatedEvent>        ReputationCacheUpdated;
        public event Action<SeasonArchiveCacheUpdatedEvent>     SeasonArchiveCacheUpdated;
        public event Action<ChatCacheUpdatedEvent>              ChatCacheUpdated;
        public event Action<ChatModerationCacheUpdatedEvent>    ChatModerationCacheUpdated;
        public event Action<MailCacheUpdatedEvent>              MailCacheUpdated;
        public event Action<KmhGrantReceivedEvent>              GrantReceived;
        public event Action<KmhNotificationReceivedEvent>      NotificationReceived;
        public event Action<KmhWorldEventFiredEvent>           WorldEventFired;
        public event Action<KmhWorldEventEndedEvent>           WorldEventEnded;

        internal void RaiseKmhServerConnected(KmhServerConnectedEvent e)
            => SafeRaise(KmhServerConnected, e, nameof(KmhServerConnected));

        internal void RaiseKmhServerDisconnected(KmhServerDisconnectedEvent e)
            => SafeRaise(KmhServerDisconnected, e, nameof(KmhServerDisconnected));

        // Raised only when goods actually land, never when the grant is merely approved.
        internal void RaiseGrantReceived(KmhGrantReceivedEvent e)
            => SafeRaise(GrantReceived, e, nameof(GrantReceived));

        internal void RaiseNotificationReceived(KmhNotificationReceivedEvent e)
            => SafeRaise(NotificationReceived, e, nameof(NotificationReceived));

        internal void RaiseWorldEventFired(KmhWorldEventFiredEvent e) => SafeRaise(WorldEventFired, e, nameof(WorldEventFired));
        internal void RaiseWorldEventEnded(KmhWorldEventEndedEvent e) => SafeRaise(WorldEventEnded, e, nameof(WorldEventEnded));

        private static void SafeRaise<T>(Action<T> evt, T payload, string name)
        {
            if (evt == null) return;
            foreach (Delegate d in evt.GetInvocationList())
            {
                try { ((Action<T>)d)(payload); }
                catch (Exception ex)
                {
                    KmhLog.Warn($"Extension event subscriber on '{name}' threw: {ex.Message}");
                }
            }
        }
    }
}
