using System;
using KMH.Sdk.Client.Events;

namespace KMH.Sdk.Client.Apis
{
    /// <summary>SDK-safe wrapper around a KMH protocol envelope received from the server.</summary>
    public interface IKmhEnvelope
    {
        string Kind { get; }
        int    Version { get; }
        int    GetInt(string key, int defaultValue = 0);
        string GetString(string key, string defaultValue = null);
        bool   GetBool(string key, bool defaultValue = false);
        T      DataAs<T>() where T : class;
    }

    /// <summary>Writes to the KMH log, each line prefixed with the extension's name.</summary>
    public interface IClientLog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Error(string message, Exception ex);
    }

    /// <summary>Transient in-game toasts; use sparingly, RimWorld players already get plenty of messages.</summary>
    public interface INotifications
    {
        /// <summary>A success toast.</summary>
        void Positive(string message);

        /// <summary>A toast for a request the server refused.</summary>
        void Rejected(string message);

        /// <summary>A neutral informational toast.</summary>
        void Neutral(string message);
    }

    /// <summary>Lifecycle and snapshot events, raised on RimWorld's main thread, so keep handlers cheap.</summary>
    public interface IKmhClientEvents
    {
        /// <summary>The handshake with a compatible KMH server completed.</summary>
        event Action<KmhServerConnectedEvent> KmhServerConnected;

        /// <summary>The connection dropped; raised before the caches are cleared.</summary>
        event Action<KmhServerDisconnectedEvent> KmhServerDisconnected;

        /// <summary>Treasury cache refreshed.</summary>
        event Action<TreasuryCacheUpdatedEvent> TreasuryCacheUpdated;

        /// <summary>Marketplace cache refreshed.</summary>
        event Action<MarketplaceCacheUpdatedEvent> MarketplaceCacheUpdated;

        /// <summary>Quest cache refreshed.</summary>
        event Action<QuestCacheUpdatedEvent> QuestCacheUpdated;

        /// <summary>The local player's guild cache refreshed.</summary>
        event Action<GuildCacheUpdatedEvent> GuildCacheUpdated;

        /// <summary>Cross-guild leaderboard cache refreshed.</summary>
        event Action<GuildLeaderboardCacheUpdatedEvent> GuildLeaderboardCacheUpdated;

        /// <summary>Player-stats leaderboard cache refreshed.</summary>
        event Action<PlayerStatsCacheUpdatedEvent> PlayerStatsCacheUpdated;

        /// <summary>Discord linked-accounts map refreshed.</summary>
        event Action<LinkedAccountsCacheUpdatedEvent> LinkedAccountsCacheUpdated;

        /// <summary>Auction cache refreshed.</summary>
        event Action<AuctionCacheUpdatedEvent> AuctionCacheUpdated;

        /// <summary>World cache refreshed (events + global quests).</summary>
        event Action<WorldCacheUpdatedEvent> WorldCacheUpdated;

        /// <summary>Want Board cache refreshed.</summary>
        event Action<WantCacheUpdatedEvent> WantCacheUpdated;

        /// <summary>Sites cache refreshed.</summary>
        event Action<SiteCacheUpdatedEvent> SiteCacheUpdated;

        /// <summary>Road network cache refreshed.</summary>
        event Action<RoadNetworkCacheUpdatedEvent> RoadNetworkCacheUpdated;

        /// <summary>Reputation cache refreshed.</summary>
        event Action<ReputationCacheUpdatedEvent> ReputationCacheUpdated;

        /// <summary>Season archive cache refreshed.</summary>
        event Action<SeasonArchiveCacheUpdatedEvent> SeasonArchiveCacheUpdated;

        /// <summary>KMH chat cache refreshed (new messages in any channel the player can see).</summary>
        event Action<ChatCacheUpdatedEvent> ChatCacheUpdated;

        /// <summary>The local player's chat block list changed.</summary>
        event Action<ChatModerationCacheUpdatedEvent> ChatModerationCacheUpdated;

        /// <summary>Player mail cache refreshed (inbox, unread count, or outgoing escrow changed).</summary>
        event Action<MailCacheUpdatedEvent> MailCacheUpdated;

        /// <summary>This player's federated silver operations changed durably on the server.</summary>
        event Action<ConnectedTreasuryUpdatedEvent> ConnectedTreasuryUpdated;

        /// <summary>The guild's federated silver operations changed durably on the server.</summary>
        event Action<ConnectedGuildUpdatedEvent> ConnectedGuildUpdated;

        /// <summary>The guild's federation - profile, members, objectives or this server's link - changed.</summary>
        event Action<FederationUpdatedEvent> FederationUpdated;

        /// <summary>The local player received a treasury withdrawal or reward (silver/items landed in the colony).</summary>
        event Action<KmhGrantReceivedEvent> GrantReceived;

        /// <summary>An offline notice arrived for the local player (auction won, listing sold, want filled, ...).</summary>
        event Action<KmhNotificationReceivedEvent> NotificationReceived;

        /// <summary>A server-wide world event (tax holiday, market boom, ...) started.</summary>
        event Action<KmhWorldEventFiredEvent> WorldEventFired;

        /// <summary>A world event ended (expired or cancelled).</summary>
        event Action<KmhWorldEventEndedEvent> WorldEventEnded;
    }
}
