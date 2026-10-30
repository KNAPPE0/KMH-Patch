using System;
using KMH.Sdk.Client.Apis;

namespace KMH.Sdk.Client
{
    /// <summary>The API handed to each extension. It exposes only SDK types, never RWT or RimWorld ones,
    /// so extension code survives either of them changing.</summary>
    public interface IKmhClientHost
    {
        /// <summary>Version of the loaded KMHPatch assembly.</summary>
        string KmhVersion { get; }

        /// <summary>Version of the loaded SDK assembly.</summary>
        string SdkVersion { get; }

        /// <summary>Colony Nexus's own Harmony id; patch under a different one, e.g. "yourname.kmhext.auctionsui".</summary>
        string KmhHarmonyId { get; }

        /// <summary>True once this session has completed the KMH handshake; false on a stock RWT server or offline.</summary>
        bool IsKmhServer { get; }

        /// <summary>This session's in-game username, or empty when not connected.</summary>
        string LocalUsername { get; }

        ITreasuryCache         Treasury        { get; }
        IMarketplaceCache      Marketplace     { get; }
        IQuestCache            Quests          { get; }
        IGuildCache            Guild           { get; }
        IGuildLeaderboardCache GuildLeaderboard { get; }
        IPlayerStatsCache      PlayerStats     { get; }
        ILinkedAccountsCache   LinkedAccounts  { get; }
        IItemLabelResolver     ItemLabels      { get; }
        IAuctionCache          Auctions        { get; }
        IWorldCache            World           { get; }

        IClientLog       Log        { get; }
        INotifications   Toast      { get; }
        IKmhClientEvents Events     { get; }

        /// <summary>Sends a payload in KMH's own envelope; false when the session is not on a KMH server.</summary>
        bool Send(string kind, object data);

        /// <summary>Handles a server-pushed kind, e.g. "yourname.auction.snapshot".
        /// A kmh.* or already-claimed kind is refused and the reason logged.</summary>
        void RegisterHandler(string kind, Action<IKmhEnvelope> handler);
    }
}
