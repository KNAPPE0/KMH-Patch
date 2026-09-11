using System;
using System.Collections.Generic;
using KMH.Sdk.Client.Apis;
using KMH.Sdk.Client.Records;
using KMHPatch.Features.Auctions;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Marketplace;
using KMHPatch.Features.PlayerStats;
using KMHPatch.Features.Quests;
using KMHPatch.Features.Treasury;
using KMHPatch.Features.World;

namespace KMHPatch.Extensibility
{
    // Stateless by design: the cache singletons hold the data, so an adapter never owns a second copy.

    internal sealed class TreasuryCacheAdapter : ITreasuryCache
    {
        public bool HasSnapshot                => TreasuryCache.HasSnapshot;
        public DateTime LastUpdatedUtc         => TreasuryCache.LastUpdatedUtc;
        public bool RequestRefresh()           => TreasuryHandler.RequestSnapshot();
        public TreasurySnapshotRecord Snapshot => Convert(TreasuryCache.Snapshot);

        private static TreasurySnapshotRecord Convert(Features.Treasury.Dto.TreasurySnapshot s)
        {
            if (s == null) return null;
            Dictionary<string, int> items = s.Items != null
                ? new Dictionary<string, int>(s.Items, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>();
            List<TreasuryTransactionRecord> tx = new List<TreasuryTransactionRecord>();
            if (s.RecentTransactions != null)
            {
                foreach (Features.Treasury.Dto.TreasuryTransaction t in s.RecentTransactions)
                {
                    tx.Add(new TreasuryTransactionRecord
                    {
                        UtcTicks    = t.UtcTicks,
                        Username    = t.Username ?? "",
                        Kind        = t.Kind     ?? "",
                        Amount      = t.Amount,
                        ItemDefName = t.ItemDefName ?? "",
                        Note        = t.Note ?? "",
                    });
                }
            }
            return new TreasurySnapshotRecord
            {
                OwnerKey          = s.OwnerKey         ?? "",
                IsGuildOwned      = s.IsGuildOwned,
                SilverBalance     = s.SilverBalance,
                LifetimeSilverIn  = s.LifetimeSilverIn,
                LifetimeSilverOut = s.LifetimeSilverOut,
                Items             = items,
                RecentTransactions= tx,
                CanDeposit        = s.CanDeposit,
                CanWithdraw       = s.CanWithdraw,
            };
        }
    }

    internal sealed class MarketplaceCacheAdapter : IMarketplaceCache
    {
        public bool HasSnapshot                              => MarketplaceCache.HasSnapshot;
        public DateTime LastUpdatedUtc                       => MarketplaceCache.LastUpdatedUtc;
        public bool RequestRefresh()                         => MarketplaceHandler.RequestSnapshot();
        public bool TryBuy(long listingId, int qty)          => MarketplaceHandler.TryBuy(listingId, qty);
        public bool TryCancel(long listingId)                => MarketplaceHandler.TryCancel(listingId);
        // The int overload has always meant whole silver, so an extension still passing 100 must keep listing at 100 silver.
        internal static int WireMilliFromSilverApi(int unitPriceSilver) => KmhSilver.ToMilli(unitPriceSilver);

        internal static decimal PublicSilverFromWireMilli(long unitPriceMilli) => unitPriceMilli / 1000m;

        public bool TryPost(string defName, int qty, int unitPriceSilver, string visibility = "public", int expiresHours = 0)
            => MarketplaceHandler.TryPost(defName, qty, WireMilliFromSilverApi(unitPriceSilver), visibility, expiresHours);

        public bool TryPost(string defName, int qty, decimal unitPriceSilver, string visibility = "public", int expiresHours = 0)
            => KmhSilver.TryToMilli(unitPriceSilver, out int milli)
               && MarketplaceHandler.TryPost(defName, qty, milli, visibility, expiresHours);

        public IReadOnlyList<MarketplaceListingRecord> Listings
        {
            get
            {
                List<MarketplaceListingRecord> result = new List<MarketplaceListingRecord>();
                Features.Marketplace.Dto.MarketplaceSnapshot s = MarketplaceCache.Snapshot;
                if (s?.Listings != null)
                {
                    foreach (Features.Marketplace.Dto.MarketplaceListing l in s.Listings)
                    {
                        result.Add(new MarketplaceListingRecord
                        {
                            Id                = l.Id,
                            SellerUsername    = l.SellerUsername    ?? "",
                            SellerTreasuryKey = l.SellerTreasuryKey ?? "",
                            ItemDefName       = l.ItemDefName       ?? "",
                            RemainingQty      = l.RemainingQty,
                            OriginalQty       = l.OriginalQty,
                            UnitPriceSilver   = l.UnitPriceSilver,
                            UnitPrice         = PublicSilverFromWireMilli(l.EffectiveMilli),
                            ListedUtcTicks    = l.ListedUtcTicks,
                            ExpiresUtcTicks   = l.ExpiresUtcTicks,
                            // Not on the patch-side DTO because the server filters before push.
                            Visibility        = "public",
                        });
                    }
                }
                return result;
            }
        }
    }

    internal sealed class QuestCacheAdapter : IQuestCache
    {
        public bool HasSnapshot          => QuestCache.HasSnapshot;
        public DateTime LastUpdatedUtc   => QuestCache.LastUpdatedUtc;
        public bool RequestRefresh()     => QuestHandler.RequestSnapshot();
        public bool TryClaim(long id)    => QuestHandler.TryClaim(id);
        public bool TrySubmit(long id)   => QuestHandler.TrySubmit(id);
        public bool TryApprove(long id)  => QuestHandler.TryApprove(id);
        public bool TryCancel(long id)   => QuestHandler.TryCancel(id);

        public bool TryPostDeliverItem(string title, string description, int bountySilver,
                                       string targetDefName, int targetQty, string visibility = "public", int expiresHours = 0)
            => QuestHandler.TryPostDeliverItem(title, description, bountySilver, targetDefName, targetQty, visibility, expiresHours);

        public bool TryPostBounty(string title, string description, int bountySilver,
                                  string visibility = "public", int expiresHours = 0)
            => QuestHandler.TryPostBounty(title, description, bountySilver, visibility, expiresHours);

        public IReadOnlyList<QuestRecord> Quests
        {
            get
            {
                List<QuestRecord> result = new List<QuestRecord>();
                Features.Quests.Dto.QuestSnapshot s = QuestCache.Snapshot;
                if (s?.Quests != null)
                {
                    foreach (Features.Quests.Dto.QuestEntry q in s.Quests)
                    {
                        result.Add(new QuestRecord
                        {
                            Id                = q.Id,
                            Kind              = q.Kind              ?? "",
                            State             = q.State             ?? "",
                            Visibility        = q.Visibility        ?? "public",
                            PosterUsername    = q.PosterUsername    ?? "",
                            PosterTreasuryKey = q.PosterTreasuryKey ?? "",
                            ClaimedByUsername = q.ClaimedByUsername ?? "",
                            Title             = q.Title             ?? "",
                            Description       = q.Description       ?? "",
                            BountySilver      = q.BountySilver,
                            TargetItemDefName = q.TargetItemDefName ?? "",
                            TargetItemQty     = q.TargetItemQty,
                            PostedUtcTicks    = q.PostedUtcTicks,
                            ExpiresUtcTicks   = q.ExpiresUtcTicks,
                        });
                    }
                }
                return result;
            }
        }
    }

    internal sealed class GuildCacheAdapter : IGuildCache
    {
        public bool HasSnapshot          => GuildCache.HasSnapshot;
        public bool InGuild              => GuildCache.InGuild;
        public DateTime LastUpdatedUtc   => GuildCache.LastUpdatedUtc;
        public bool RequestRefresh()     => GuildHandler.RequestSnapshot();

        public GuildSnapshotRecord Snapshot
        {
            get
            {
                Features.Guilds.Dto.GuildSnapshot g = GuildCache.Guild;
                if (g == null) return null;
                List<GuildMemberRecord> members = new List<GuildMemberRecord>();
                if (g.Members != null)
                {
                    foreach (Features.Guilds.Dto.GuildMemberDto m in g.Members)
                    {
                        members.Add(new GuildMemberRecord
                        {
                            Username          = m.Username ?? "",
                            Rank              = m.Rank     ?? "",
                            JoinedUtcTicks    = m.JoinedUtcTicks,
                            SilverContributed = m.SilverContributed,
                            QuestsCompleted   = m.QuestsCompleted,
                        });
                    }
                }
                return new GuildSnapshotRecord
                {
                    Name           = g.Name ?? "",
                    Motd           = g.Motd ?? "",
                    // Not on the patch-side DTO, so it comes from the leaderboard cache or not at all.
                    TreasurySilver = LookupGuildTreasurySilver(g.Name),
                    Members        = members,
                };
            }
        }

        private static long LookupGuildTreasurySilver(string guildName)
        {
            if (string.IsNullOrEmpty(guildName) || !GuildLeaderboardCache.HasSnapshot) return 0;
            Features.Guilds.Dto.GuildLeaderboardSnapshot s = GuildLeaderboardCache.Snapshot;
            if (s?.Guilds == null) return 0;
            foreach (Features.Guilds.Dto.GuildLeaderboardEntry e in s.Guilds)
            {
                if (string.Equals(e.Name, guildName, StringComparison.OrdinalIgnoreCase))
                    return e.TreasurySilver;
            }
            return 0;
        }
    }

    internal sealed class GuildLeaderboardCacheAdapter : IGuildLeaderboardCache
    {
        public bool HasSnapshot          => GuildLeaderboardCache.HasSnapshot;
        public DateTime LastUpdatedUtc   => GuildLeaderboardCache.LastUpdatedUtc;
        public bool RequestRefresh()     => GuildHandler.RequestLeaderboard();

        public IReadOnlyList<GuildSummaryRecord> Guilds
        {
            get
            {
                List<GuildSummaryRecord> result = new List<GuildSummaryRecord>();
                Features.Guilds.Dto.GuildLeaderboardSnapshot s = GuildLeaderboardCache.Snapshot;
                if (s?.Guilds != null)
                {
                    foreach (Features.Guilds.Dto.GuildLeaderboardEntry g in s.Guilds)
                    {
                        result.Add(new GuildSummaryRecord
                        {
                            Name           = g.Name ?? "",
                            MemberCount    = g.MemberCount,
                            TreasurySilver = g.TreasurySilver,
                        });
                    }
                }
                return result;
            }
        }
    }

    internal sealed class PlayerStatsCacheAdapter : IPlayerStatsCache
    {
        public bool HasSnapshot          => PlayerStatsCache.LastUpdatedUtc > DateTime.MinValue;
        public DateTime LastUpdatedUtc   => PlayerStatsCache.LastUpdatedUtc;
        public bool RequestRefresh()     => PlayerStatsHandler.RequestSnapshot();

        public IReadOnlyList<PlayerStatRecord> Entries
        {
            get
            {
                List<PlayerStatRecord> result = new List<PlayerStatRecord>();
                List<Features.PlayerStats.Dto.PlayerLeaderboardEntry> src = PlayerStatsCache.Entries;
                if (src != null)
                {
                    foreach (Features.PlayerStats.Dto.PlayerLeaderboardEntry e in src)
                    {
                        result.Add(new PlayerStatRecord
                        {
                            Username          = e.Username ?? "",
                            FirstSeenUtcTicks = e.FirstSeenUtcTicks,
                            SilverDonated     = e.SilverDonated,
                            SalesEarned       = e.SalesEarned,
                            MarketplaceSales  = e.MarketplaceSales,
                            QuestsCompleted   = e.QuestsCompleted,
                            QuestsPosted      = e.QuestsPosted,
                            SitesBuilt        = e.SitesBuilt,
                            SitesOwned        = e.SitesOwned,
                            OutpostsHeld      = e.OutpostsHeld,
                            FrontierCaptures  = e.FrontierCaptures,
                            WorkerXp          = e.WorkerXp,
                            EconomyScore      = e.EconomyScore,
                        });
                    }
                }
                return result;
            }
        }
    }

    internal sealed class LinkedAccountsCacheAdapter : ILinkedAccountsCache
    {
        public bool HasSnapshot                                   => LinkedAccountsCache.HasSnapshot;
        public DateTime LastUpdatedUtc                            => LinkedAccountsCache.LastUpdatedUtc;
        public bool   IsLinked(string username)                   => LinkedAccountsCache.IsLinked(username);
        public string DiscordDisplayFor(string username)          => LinkedAccountsCache.DiscordNameFor(username);
        public string FormatUsername(string username)             => LinkedAccountsCache.Format(username);
    }

    internal sealed class ItemLabelResolverAdapter : IItemLabelResolver
    {
        public string LabelFor(string defName)                                    => UI.ItemLabels.ResolveLabel(defName);
        public string ResolveStuffedLabel(string itemDefName, string stuffDefName) => UI.ItemLabels.ResolveStuffedLabel(itemDefName, stuffDefName);
    }

    internal sealed class AuctionCacheAdapter : IAuctionCache
    {
        public bool HasSnapshot        => AuctionCache.HasSnapshot;
        public DateTime LastUpdatedUtc => AuctionCache.LastUpdatedUtc;
        public bool RequestRefresh()   => AuctionHandler.RequestSnapshot();

        public bool TryPost(string itemDefName, string stuffDefName, int quality, int qty,
                            long startingBid, long minIncrement, long buyoutSilver, int durationHours, string visibility = "public")
            => AuctionHandler.TryPost(itemDefName, stuffDefName, quality, qty, startingBid, minIncrement, buyoutSilver, durationHours, visibility);

        public bool TryBid(long auctionId, long amount) => AuctionHandler.TryBid(auctionId, amount);
        public bool TryCancel(long auctionId)           => AuctionHandler.TryCancel(auctionId);

        public IReadOnlyList<AuctionRecord> Auctions
        {
            get
            {
                List<AuctionRecord> result = new List<AuctionRecord>();
                Features.Auctions.Dto.AuctionSnapshot s = AuctionCache.Snapshot;
                if (s?.Auctions != null)
                {
                    foreach (Features.Auctions.Dto.AuctionDto a in s.Auctions)
                    {
                        result.Add(new AuctionRecord
                        {
                            Id                = a.Id,
                            SellerUsername    = a.SellerUsername    ?? "",
                            SellerTreasuryKey = a.SellerTreasuryKey ?? "",
                            ItemDefName       = a.ItemDefName       ?? "",
                            StuffDefName      = a.StuffDefName      ?? "",
                            QualityIndex      = a.QualityIndex,
                            Qty               = a.Qty,
                            StartingBid       = a.StartingBid,
                            MinIncrement      = a.MinIncrement,
                            BuyoutSilver      = a.BuyoutSilver,
                            CurrentBid        = a.CurrentBid,
                            HighBidder        = a.HighBidder        ?? "",
                            BidCount          = a.BidCount,
                            ListedUtcTicks    = a.ListedUtcTicks,
                            EndsUtcTicks      = a.EndsUtcTicks,
                            Visibility        = a.Visibility        ?? "public",
                        });
                    }
                }
                return result;
            }
        }
    }

    internal sealed class WorldCacheAdapter : IWorldCache
    {
        public bool HasSnapshot        => WorldCache.HasSnapshot;
        public DateTime LastUpdatedUtc => WorldCache.LastUpdatedUtc;
        public bool RequestRefresh()   => WorldHandler.RequestSnapshot();
        public bool TryDeliver(long questId, string targetDefName, int qty) => WorldHandler.TryDeliver(questId, targetDefName, qty);

        public IReadOnlyList<WorldEventRecord> Events
        {
            get
            {
                List<WorldEventRecord> result = new List<WorldEventRecord>();
                Features.World.Dto.WorldSnapshot s = WorldCache.Snapshot;
                if (s?.Events != null)
                {
                    foreach (Features.World.Dto.WorldEventDto e in s.Events)
                    {
                        result.Add(new WorldEventRecord
                        {
                            Id              = e.Id,
                            Type            = e.Type        ?? "",
                            Title           = e.Title       ?? "",
                            Description     = e.Description ?? "",
                            Magnitude       = e.Magnitude,
                            Target          = e.Target      ?? "",
                            StartedUtcTicks = e.StartedUtcTicks,
                            EndsUtcTicks    = e.EndsUtcTicks,
                        });
                    }
                }
                return result;
            }
        }

        public IReadOnlyList<ServerQuestRecord> ServerQuests
        {
            get
            {
                List<ServerQuestRecord> result = new List<ServerQuestRecord>();
                Features.World.Dto.WorldSnapshot s = WorldCache.Snapshot;
                if (s?.ServerQuests != null)
                {
                    foreach (Features.World.Dto.ServerQuestDto q in s.ServerQuests)
                    {
                        result.Add(new ServerQuestRecord
                        {
                            Id            = q.Id,
                            Kind          = q.Kind          ?? "",
                            Objective     = q.Objective     ?? "",
                            Title         = q.Title         ?? "",
                            Description   = q.Description   ?? "",
                            TargetDefName = q.TargetDefName ?? "",
                            GoalQty       = q.GoalQty,
                            ProgressQty   = q.ProgressQty,
                            RewardPool    = q.RewardPool,
                            State         = q.State         ?? "",
                            Winner        = q.Winner        ?? "",
                            EndsUtcTicks  = q.EndsUtcTicks,
                            Contributors  = q.Contributors != null
                                ? new Dictionary<string, int>(q.Contributors, StringComparer.OrdinalIgnoreCase)
                                : new Dictionary<string, int>(),
                        });
                    }
                }
                return result;
            }
        }
    }
}
