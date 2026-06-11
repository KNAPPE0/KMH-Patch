using System.Collections.Generic;
using KMH.Sdk.Client.Records;

namespace KMH.Sdk.Client.Apis
{
    // Read-only views over the client-side caches. Each cache is populated by the patch from server-pushed
    // snapshots. Extensions observe via these interfaces; mutations go through the wire (see IKmhClientHost.Send
    // and the action helpers on each cache)
    //
    // None of these interfaces expose mutating operations on the cache itself - the cache is a *projection* of
    // authoritative server state, never the source of truth. Extensions that want to mutate
    // marketplace/treasury/etc state should fire wire kinds via IKmhClientHost.Send and let the server respond with
    // a fresh snapshot

    public interface ITreasuryCache
    {
        bool HasSnapshot { get; }
        TreasurySnapshotRecord Snapshot { get; }
        System.DateTime LastUpdatedUtc { get; }

        /// <summary>Ask the server for a fresh treasury snapshot. Returns false if not connected to a KMH server.</summary>
        bool RequestRefresh();
    }

    public interface IMarketplaceCache
    {
        bool HasSnapshot { get; }
        IReadOnlyList<MarketplaceListingRecord> Listings { get; }
        System.DateTime LastUpdatedUtc { get; }

        bool RequestRefresh();

        /// <summary>Send a buy intent. Server responds with snapshot + treasury push.</summary>
        bool TryBuy(long listingId, int qty);

        /// <summary>Cancel one of your own listings.</summary>
        bool TryCancel(long listingId);

        /// <summary>Post a new listing. Items get escrowed from your treasury.</summary>
        bool TryPost(string defName, int qty, int unitPriceSilver,
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

        /// <summary>
        /// Format a username with the standard "Discord-aware" treatment
        /// (linked users get the Discord brand color in RimWorld rich-text).
        /// Safe to call for unknown / unlinked usernames.
        /// </summary>
        string FormatUsername(string username);
    }

    public interface IItemLabelResolver
    {
        /// <summary>Look up a friendly label for a RimWorld defName via the local DefDatabase.</summary>
        string LabelFor(string defName);

        /// <summary>Combine stuff + item labels ("plasteel" + "knife" → "plasteel knife"). Falls back to plain label when stuff is empty.</summary>
        string ResolveStuffedLabel(string itemDefName, string stuffDefName);
    }
}
