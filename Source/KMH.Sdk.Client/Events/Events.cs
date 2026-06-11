namespace KMH.Sdk.Client.Events
{
    // Immutable event payloads for IKmhClientEvents. Cache-updated events are tag-only (no payload data) -
    // extensions read the refreshed snapshot via the corresponding cache property on IKmhClientHost

    public sealed class KmhServerConnectedEvent
    {
        public int    ServerProtocolVersion { get; init; }
        public string Endpoint              { get; init; } = "";
    }

    public sealed class KmhServerDisconnectedEvent
    {
        public string Reason { get; init; } = "";
    }

    public sealed class TreasuryCacheUpdatedEvent          { }
    public sealed class MarketplaceCacheUpdatedEvent       { }
    public sealed class QuestCacheUpdatedEvent             { }
    public sealed class GuildCacheUpdatedEvent             { }
    public sealed class GuildLeaderboardCacheUpdatedEvent  { }
    public sealed class PlayerStatsCacheUpdatedEvent       { }
    public sealed class LinkedAccountsCacheUpdatedEvent    { }
}
