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
    }
}
