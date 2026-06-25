using System;
using KMH.Sdk.Client;
using KMH.Sdk.Client.Apis;
using KMHPatch.Diagnostics;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Extensibility
{
    // IKmhClientHost implementation handed to each loaded extension. Each extension gets its own host instance so
    // the IClientLog prefix can be extension-specific
    internal sealed class KmhClientHost : IKmhClientHost
    {
        private readonly string _extensionName;

        public KmhClientHost(string extensionName)
        {
            _extensionName   = extensionName;
            Log              = new ClientLogAdapter(extensionName);
            Toast            = new NotificationsAdapter();
            Treasury         = new TreasuryCacheAdapter();
            Marketplace      = new MarketplaceCacheAdapter();
            Quests           = new QuestCacheAdapter();
            Guild            = new GuildCacheAdapter();
            GuildLeaderboard = new GuildLeaderboardCacheAdapter();
            PlayerStats      = new PlayerStatsCacheAdapter();
            LinkedAccounts   = new LinkedAccountsCacheAdapter();
            ItemLabels       = new ItemLabelResolverAdapter();
            Auctions         = new AuctionCacheAdapter();
            World            = new WorldCacheAdapter();
            Events           = KmhClientEventBus.Instance;
        }

        public string KmhVersion    => typeof(KMHPatchMod).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        public string SdkVersion    => typeof(IKmhClientHost).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        public string KmhHarmonyId  => Constants.HarmonyId;

        public bool   IsKmhServer   => KmhDispatcher.IsKmhServer;
        public string LocalUsername => SessionHandler.Username ?? "";

        public ITreasuryCache         Treasury         { get; }
        public IMarketplaceCache      Marketplace      { get; }
        public IQuestCache            Quests           { get; }
        public IGuildCache            Guild            { get; }
        public IGuildLeaderboardCache GuildLeaderboard { get; }
        public IPlayerStatsCache      PlayerStats      { get; }
        public ILinkedAccountsCache   LinkedAccounts   { get; }
        public IItemLabelResolver     ItemLabels       { get; }
        public IAuctionCache          Auctions         { get; }
        public IWorldCache            World            { get; }

        public IClientLog             Log              { get; }
        public INotifications         Toast            { get; }
        public IKmhClientEvents       Events           { get; }

        public bool Send(string kind, object data) => KmhDispatcher.Send(kind, data);

        // Reserved wire-kind namespace - KMH core owns "kmh.". Extensions registering there could shadow core
        // snapshot handling, so we refuse it. Every other kind stays open for extensions
        private const string ReservedKindPrefix = "kmh.";

        public void RegisterHandler(string kind, Action<IKmhEnvelope> handler)
        {
            if (string.IsNullOrEmpty(kind) || handler == null) return;

            if (kind.StartsWith(ReservedKindPrefix, StringComparison.OrdinalIgnoreCase))
            {
                KmhLog.Warn(
                    $"[ext:{_extensionName}] refused to register reserved kind '{kind}'. " +
                    $"The '{ReservedKindPrefix}' namespace belongs to KMH core - namespace your own kinds " +
                    $"under your extension name.");
                return;
            }

            if (KmhDispatcher.IsRegistered(kind))
            {
                KmhLog.Warn(
                    $"[ext:{_extensionName}] refused to register kind '{kind}' - already claimed by " +
                    $"KMH or another extension. Pick a kind unique to your extension.");
                return;
            }

            KmhDispatcher.RegisterHandler(kind, env =>
            {
                try { handler(new KmhEnvelopeAdapter(env)); }
                catch (Exception ex)
                {
                    KmhLog.Warn($"[ext:{_extensionName}] handler for '{kind}' threw: {ex.Message}");
                }
            });
        }

        // -- Adapter classes: SDK contract <-> KMHPatch internals --

        private sealed class ClientLogAdapter : IClientLog
        {
            private readonly string _prefix;
            public ClientLogAdapter(string ext) => _prefix = $"[ext:{ext}]";
            public void Info(string m)                  => KmhLog.Info ($"{_prefix} {m}");
            public void Warn(string m)                  => KmhLog.Warn ($"{_prefix} {m}");
            public void Error(string m)                 => KmhLog.Error($"{_prefix} {m}");
            public void Error(string m, Exception ex)   => KmhLog.Error($"{_prefix} {m}", ex);
        }

        private sealed class NotificationsAdapter : INotifications
        {
            public void Positive(string m) => KmhNotifications.Positive(m);
            public void Rejected(string m) => KmhNotifications.Rejected(m);
            public void Neutral(string m)  => KmhNotifications.Neutral(m);
        }

        internal sealed class KmhEnvelopeAdapter : IKmhEnvelope
        {
            private readonly KmhEnvelope _env;
            public KmhEnvelopeAdapter(KmhEnvelope env) => _env = env;
            public string Kind                                              => _env?.Kind ?? "";
            public int    Version                                           => _env?.Version ?? 0;
            public int    GetInt(string key, int defaultValue = 0)          => _env?.GetInt(key, defaultValue) ?? defaultValue;
            public string GetString(string key, string defaultValue = null) => _env?.GetString(key, defaultValue) ?? defaultValue;
            public bool   GetBool(string key, bool defaultValue = false)    => _env?.GetBool(key, defaultValue) ?? defaultValue;
            public T      DataAs<T>() where T : class                       => _env?.DataAs<T>();
        }
    }
}
