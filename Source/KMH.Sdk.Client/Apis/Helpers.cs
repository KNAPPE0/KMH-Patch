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

    /// <summary>
    /// Pipes extension messages through KMH's logger so output is
    /// prefixed with the extension's name + lands in the dedicated
    /// KMH log file alongside Verse.Log.
    /// </summary>
    public interface IClientLog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Error(string message, Exception ex);
    }

    /// <summary>
    /// Transient in-game toast notifications. Use sparingly - RimWorld
    /// players have lots of messages already.
    /// </summary>
    public interface INotifications
    {
        /// <summary>Green "things went well" toast.</summary>
        void Positive(string message);

        /// <summary>Yellow "request rejected by server" toast.</summary>
        void Rejected(string message);

        /// <summary>Plain neutral toast for informational events.</summary>
        void Neutral(string message);
    }

    /// <summary>Subscribe to client-side lifecycle + snapshot events.</summary>
    /// <remarks>
    /// Handlers fire on RimWorld's main thread (snapshot callbacks
    /// arrive via the patch's main-thread queue). Long-running work
    /// should be queued to your own background worker.
    /// </remarks>
    public interface IKmhClientEvents
    {
        /// <summary>Handshake completed - KMH server confirmed compatible.</summary>
        event Action<KmhServerConnectedEvent> KmhServerConnected;

        /// <summary>Connection dropped. Caches will be reset shortly after.</summary>
        event Action<KmhServerDisconnectedEvent> KmhServerDisconnected;

        /// <summary>Treasury cache refreshed (new snapshot from server).</summary>
        event Action<TreasuryCacheUpdatedEvent> TreasuryCacheUpdated;

        /// <summary>Marketplace cache refreshed.</summary>
        event Action<MarketplaceCacheUpdatedEvent> MarketplaceCacheUpdated;

        /// <summary>Quest cache refreshed.</summary>
        event Action<QuestCacheUpdatedEvent> QuestCacheUpdated;

        /// <summary>Caller's guild snapshot refreshed.</summary>
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
