namespace KMH.Sdk.Client.Events
{
    /// <summary>Payloads for IKmhClientEvents; cache-updated events are tag-only, so read the matching cache.</summary>

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
    public sealed class AuctionCacheUpdatedEvent           { }
    public sealed class WorldCacheUpdatedEvent             { }
    public sealed class WantCacheUpdatedEvent              { }
    public sealed class SiteCacheUpdatedEvent              { }
    public sealed class RoadNetworkCacheUpdatedEvent       { }
    public sealed class ReputationCacheUpdatedEvent        { }
    public sealed class SeasonArchiveCacheUpdatedEvent     { }
    public sealed class ChatCacheUpdatedEvent              { }
    public sealed class ChatModerationCacheUpdatedEvent    { }
    public sealed class MailCacheUpdatedEvent              { }

    /// <summary>Silver or items materialised for the local player, whether granted immediately or held and delivered later.</summary>
    public sealed class KmhGrantReceivedEvent
    {
        public string Kind        { get; init; } = "";   // "silver" | "item" | "item_payloads"
        public long   Silver      { get; init; }          // silver amount (Kind == "silver")
        public string ItemDefName { get; init; } = "";    // composed item key (Kind == "item"); empty for payloads
        public int    Quantity    { get; init; }          // unit count for items/payloads
    }

    /// <summary>One offline notice for the local player, fired as the "while you were away" letter is shown.</summary>
    public sealed class KmhNotificationReceivedEvent
    {
        public string Title    { get; init; } = "";
        public string Body     { get; init; } = "";
        public string Tone     { get; init; } = "";   // "positive" | "negative" | "neutral"
        public long   UtcTicks { get; init; }
    }

    /// <summary>A world event started; not fired on reconnect for events already running.</summary>
    public sealed class KmhWorldEventFiredEvent
    {
        public long   Id           { get; init; }
        public string Type         { get; init; } = "";   // "tax_holiday", "market_boom", ...
        public string Title        { get; init; } = "";
        public double Magnitude    { get; init; }
        public long   EndsUtcTicks { get; init; }
    }

    /// <summary>A world event ended by expiry or cancellation; pairs with KmhWorldEventFiredEvent by Id.</summary>
    public sealed class KmhWorldEventEndedEvent
    {
        public long   Id    { get; init; }
        public string Type  { get; init; } = "";
        public string Title { get; init; } = "";
    }
}
