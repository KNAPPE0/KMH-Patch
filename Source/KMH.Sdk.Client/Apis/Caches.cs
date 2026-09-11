using System.Collections.Generic;
using KMH.Sdk.Client.Records;

namespace KMH.Sdk.Client.Apis
{
    /// <summary>Read-only cache views for extensions; mutations go through the wire and fresh server snapshots.</summary>
    public interface ITreasuryCache
    {
        bool HasSnapshot { get; }
        TreasurySnapshotRecord Snapshot { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool RequestRefresh();
    }

    public interface IMarketplaceCache
    {
        bool HasSnapshot { get; }
        IReadOnlyList<MarketplaceListingRecord> Listings { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool RequestRefresh();

        bool TryBuy(long listingId, int qty);

        bool TryCancel(long listingId);

        /// <summary>Post a listing priced in whole silver.</summary>
        bool TryPost(string defName, int qty, int unitPriceSilver,
                     string visibility = "public", int expiresHours = 0);

        /// <summary>Post a listing priced in silver, to at most three decimal places; a finer price is refused.</summary>
        bool TryPost(string defName, int qty, decimal unitPriceSilver,
                     string visibility = "public", int expiresHours = 0);
    }

    public interface IQuestCache
    {
        bool HasSnapshot { get; }
        IReadOnlyList<QuestRecord> Quests { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool RequestRefresh();

        bool TryClaim(long questId);
        bool TrySubmit(long questId);
        bool TryApprove(long questId);
        bool TryCancel(long questId);

        bool TryPostDeliverItem(string title, string description, int bountySilver,
                                string targetDefName, int targetQty,
                                string visibility = "public", int expiresHours = 0);
        bool TryPostBounty(string title, string description, int bountySilver,
                           string visibility = "public", int expiresHours = 0);
    }

    public interface IGuildCache
    {
        bool HasSnapshot { get; }
        bool InGuild     { get; }
        GuildSnapshotRecord Snapshot { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool RequestRefresh();
    }

    public interface IGuildLeaderboardCache
    {
        bool HasSnapshot { get; }
        IReadOnlyList<GuildSummaryRecord> Guilds { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool RequestRefresh();
    }

    public interface IPlayerStatsCache
    {
        bool HasSnapshot { get; }
        IReadOnlyList<PlayerStatRecord> Entries { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool RequestRefresh();
    }

    public interface ILinkedAccountsCache
    {
        bool HasSnapshot { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool   IsLinked(string username);
        string DiscordDisplayFor(string username);

        string FormatUsername(string username);
    }

    public interface IItemLabelResolver
    {
        string LabelFor(string defName);

        string ResolveStuffedLabel(string itemDefName, string stuffDefName);
    }

    public interface IAuctionCache
    {
        bool HasSnapshot { get; }
        IReadOnlyList<AuctionRecord> Auctions { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool RequestRefresh();

        bool TryPost(string itemDefName, string stuffDefName, int quality, int qty,
                     long startingBid, long minIncrement, long buyoutSilver, int durationHours,
                     string visibility = "public");

        bool TryBid(long auctionId, long amount);

        bool TryCancel(long auctionId);
    }

    public interface IWorldCache
    {
        bool HasSnapshot { get; }
        IReadOnlyList<WorldEventRecord> Events { get; }

        IReadOnlyList<ServerQuestRecord> ServerQuests { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool RequestRefresh();

        bool TryDeliver(long questId, string targetDefName, int qty);
    }
}
