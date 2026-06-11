using System;
using KMH.Sdk.Client.Apis;

namespace KMH.Sdk.Client
{
    /// <summary>
    /// Stable API surface handed to every loaded client extension.
    /// Mirrors <c>KMH.Sdk.Server.IKmhServerHost</c> on the patch side.
    /// </summary>
    /// <remarks>
    /// The SDK never exposes raw RWT or RimWorld types - extensions
    /// get SDK-defined records / interfaces only. That insulates
    /// extension code from API drift in either dependency.
    /// </remarks>
    public interface IKmhClientHost
    {
        // -- Identity / diagnostics --

        /// <summary>Version of the KMHPatch assembly currently loaded.</summary>
        string KmhVersion { get; }

        /// <summary>Version of the SDK assembly currently loaded.</summary>
        string SdkVersion { get; }

        /// <summary>
        /// Harmony instance id KMH-Patch uses for its own patches.
        /// If your extension installs Harmony patches, use a
        /// DIFFERENT id (e.g.
        /// <c>"yourname.kmhext.auctionsui"</c>) so the Harmony tracker
        /// keeps the two patch sets distinguishable in logs.
        /// </summary>
        string KmhHarmonyId { get; }

        /// <summary>
        /// True once the current session has handshaken with a KMH-
        /// enabled server. False on stock-RWT servers / not connected.
        /// </summary>
        bool IsKmhServer { get; }

        /// <summary>
        /// The in-game username for the current session, or empty
        /// string when not connected.
        /// </summary>
        string LocalUsername { get; }

        // -- Stable APIs --

        ITreasuryCache         Treasury        { get; }
        IMarketplaceCache      Marketplace     { get; }
        IQuestCache            Quests          { get; }
        IGuildCache            Guild           { get; }
        IGuildLeaderboardCache GuildLeaderboard { get; }
        IPlayerStatsCache      PlayerStats     { get; }
        ILinkedAccountsCache   LinkedAccounts  { get; }
        IItemLabelResolver     ItemLabels      { get; }

        IClientLog       Log        { get; }
        INotifications   Toast      { get; }
        IKmhClientEvents Events     { get; }

        // -- Protocol surface --

        /// <summary>
        /// Send a typed payload to the server. Returns false if the
        /// session isn't on a KMH server. Same envelope shape KMH's
        /// own features use.
        /// </summary>
        bool Send(string kind, object data);

        /// <summary>
        /// Register a handler for a wire kind pushed by the server.
        /// Namespace your kinds (e.g. <c>"yourname.auction.snapshot"</c>)
        /// so they don't collide with KMH's own <c>kmh.*</c>.
        /// </summary>
        void RegisterHandler(string kind, Action<IKmhEnvelope> handler);
    }
}
