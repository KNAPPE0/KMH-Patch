using System;
using KMH.Sdk.Client.Apis;
using KMH.Sdk.Client.Events;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Marketplace;
using KMHPatch.Features.PlayerStats;
using KMHPatch.Features.Quests;
using KMHPatch.Features.Treasury;

namespace KMHPatch.Extensibility
{
    // Singleton event hub. Implements IKmhClientEvents so every host instance hands extensions the same shared bus
    //
    // The bus subscribes to each cache's Updated event in its static constructor and re-fires the SDK-typed payload
    // to extension subscribers. Subscriber-throw protection guards dispatch
    internal sealed class KmhClientEventBus : IKmhClientEvents
    {
        public static KmhClientEventBus Instance { get; } = new KmhClientEventBus();

        private KmhClientEventBus()
        {
            // Wire one source -> bus subscription per cache. Idempotent - singleton ctor runs once, and the cache
            // events are ref-counted so multiple subscriptions would still fire once per Apply but eat memory
            // unnecessarily
            TreasuryCache.Updated         += () => SafeRaise(TreasuryCacheUpdated,         new TreasuryCacheUpdatedEvent(),         nameof(TreasuryCacheUpdated));
            MarketplaceCache.Updated      += () => SafeRaise(MarketplaceCacheUpdated,      new MarketplaceCacheUpdatedEvent(),      nameof(MarketplaceCacheUpdated));
            QuestCache.Updated            += () => SafeRaise(QuestCacheUpdated,            new QuestCacheUpdatedEvent(),            nameof(QuestCacheUpdated));
            GuildCache.Updated            += () => SafeRaise(GuildCacheUpdated,            new GuildCacheUpdatedEvent(),            nameof(GuildCacheUpdated));
            GuildLeaderboardCache.Updated += () => SafeRaise(GuildLeaderboardCacheUpdated, new GuildLeaderboardCacheUpdatedEvent(), nameof(GuildLeaderboardCacheUpdated));
            PlayerStatsCache.Updated      += () => SafeRaise(PlayerStatsCacheUpdated,      new PlayerStatsCacheUpdatedEvent(),      nameof(PlayerStatsCacheUpdated));
            LinkedAccountsCache.Updated   += () => SafeRaise(LinkedAccountsCacheUpdated,   new LinkedAccountsCacheUpdatedEvent(),   nameof(LinkedAccountsCacheUpdated));
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

        // Called by KmhHandshakeHandler when the server's Hello arrives.
        internal void RaiseKmhServerConnected(KmhServerConnectedEvent e)
            => SafeRaise(KmhServerConnected, e, nameof(KmhServerConnected));

        // Called by Patch_DisconnectionManager_KmhDisconnect.
        internal void RaiseKmhServerDisconnected(KmhServerDisconnectedEvent e)
            => SafeRaise(KmhServerDisconnected, e, nameof(KmhServerDisconnected));

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
