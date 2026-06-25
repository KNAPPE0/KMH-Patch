using System.Collections.Generic;

namespace KMH.Sdk.Client.Records
{
    // Immutable record types returned by client-side cache readers. These mirror the server-side SDK records but
    // live in the patch assembly. Server <-> client cache shapes are kept aligned by hand - both come from the same
    // wire DTOs

    public sealed class TreasurySnapshotRecord
    {
        public string OwnerKey          { get; init; } = "";
        public bool   IsGuildOwned      { get; init; }
        public int    SilverBalance     { get; init; }
        public long   LifetimeSilverIn  { get; init; }
        public long   LifetimeSilverOut { get; init; }
        public IReadOnlyDictionary<string, int> Items { get; init; }
            = new Dictionary<string, int>();
        public IReadOnlyList<TreasuryTransactionRecord> RecentTransactions { get; init; }
            = new List<TreasuryTransactionRecord>();
        public bool   CanDeposit        { get; init; }
        public bool   CanWithdraw       { get; init; }
    }

    public sealed class TreasuryTransactionRecord
    {
        public long   UtcTicks    { get; init; }
        public string Username    { get; init; } = "";
        public string Kind        { get; init; } = "";
        public int    Amount      { get; init; }
        public string ItemDefName { get; init; } = "";
        public string Note        { get; init; } = "";
    }

    public sealed class MarketplaceListingRecord
    {
        public long   Id                { get; init; }
        public string SellerUsername    { get; init; } = "";
        public string SellerTreasuryKey { get; init; } = "";
        public string ItemDefName       { get; init; } = "";
        public int    RemainingQty      { get; init; }
        public int    OriginalQty       { get; init; }
        public int    UnitPriceSilver   { get; init; }
        public long   ListedUtcTicks    { get; init; }
        public long   ExpiresUtcTicks   { get; init; }
        public string Visibility        { get; init; } = "public";
    }

    public sealed class QuestRecord
    {
        public long   Id                { get; init; }
        public string Kind              { get; init; } = "";
        public string State             { get; init; } = "";
        public string Visibility        { get; init; } = "public";
        public string PosterUsername    { get; init; } = "";
        public string PosterTreasuryKey { get; init; } = "";
        public string ClaimedByUsername { get; init; } = "";
        public string Title             { get; init; } = "";
        public string Description       { get; init; } = "";
        public int    BountySilver      { get; init; }
        public string TargetItemDefName { get; init; } = "";
        public int    TargetItemQty     { get; init; }
        public long   PostedUtcTicks    { get; init; }
        public long   ExpiresUtcTicks   { get; init; }
    }

    public sealed class GuildSnapshotRecord
    {
        public string Name           { get; init; } = "";
        public string Motd           { get; init; } = "";
        public long   TreasurySilver { get; init; }
        public IReadOnlyList<GuildMemberRecord> Members { get; init; }
            = new List<GuildMemberRecord>();
    }

    public sealed class GuildMemberRecord
    {
        public string Username        { get; init; } = "";
        public string Rank            { get; init; } = "";   // Member / Officer / Moderator / Admin
        public long   JoinedUtcTicks  { get; init; }
        public long   SilverContributed { get; init; }
        public int    QuestsCompleted { get; init; }
    }

    public sealed class GuildSummaryRecord
    {
        public string Name           { get; init; } = "";
        public int    MemberCount    { get; init; }
        public long   TreasurySilver { get; init; }
    }

    public sealed class PlayerStatRecord
    {
        public string Username           { get; init; } = "";
        public long   FirstSeenUtcTicks  { get; init; }
        public long   SilverDonated      { get; init; }
        public long   SalesEarned        { get; init; }
        public int    MarketplaceSales   { get; init; }
        public int    QuestsCompleted    { get; init; }
        public int    QuestsPosted       { get; init; }
        public int    SitesBuilt         { get; init; }
        public long   WorkerXp           { get; init; }
        public long   EconomyScore       { get; init; }
    }

    public sealed class AuctionRecord
    {
        public long   Id                { get; init; }
        public string SellerUsername    { get; init; } = "";
        public string SellerTreasuryKey { get; init; } = "";
        public string ItemDefName       { get; init; } = "";
        public string StuffDefName      { get; init; } = "";
        public int    QualityIndex      { get; init; }
        public int    Qty               { get; init; }
        public long   StartingBid       { get; init; }
        public long   MinIncrement      { get; init; }
        public long   BuyoutSilver      { get; init; }   // 0 = no buyout
        public long   CurrentBid        { get; init; }   // 0 = no bids yet
        public string HighBidder        { get; init; } = "";
        public int    BidCount          { get; init; }
        public long   ListedUtcTicks    { get; init; }
        public long   EndsUtcTicks      { get; init; }
        public string Visibility        { get; init; } = "public";
    }

    public sealed class WorldEventRecord
    {
        public long   Id              { get; init; }
        public string Type            { get; init; } = "";
        public string Title           { get; init; } = "";
        public string Description     { get; init; } = "";
        public double Magnitude       { get; init; }
        public string Target          { get; init; } = "";
        public long   StartedUtcTicks { get; init; }
        public long   EndsUtcTicks    { get; init; }
    }

    public sealed class ServerQuestRecord
    {
        public long   Id            { get; init; }
        public string Kind          { get; init; } = "";   // cooperative / competitive
        public string Objective     { get; init; } = "";   // hunt / build / deliver
        public string Title         { get; init; } = "";
        public string Description   { get; init; } = "";
        public string TargetDefName { get; init; } = "";
        public int    GoalQty       { get; init; }
        public int    ProgressQty   { get; init; }
        public long   RewardPool    { get; init; }
        public string State         { get; init; } = "";   // active / completed / expired
        public string Winner        { get; init; } = "";
        public long   EndsUtcTicks  { get; init; }
        public IReadOnlyDictionary<string, int> Contributors { get; init; }
            = new Dictionary<string, int>();
    }
}
