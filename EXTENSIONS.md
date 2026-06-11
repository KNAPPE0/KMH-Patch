# KMH Client SDK - extensions for the patch mod

A stable public API for writing client-side (in-game) extensions
that plug into KMH-Patch as standalone RimWorld mods. The companion
to the server-side `KMH.Sdk.Server` (documented in
KMH-Server-Addon/EXTENSIONS.md).


## What you can do with it

- Read every KMH cache (treasury, marketplace, quest board, guild,
  cross-guild leaderboard, player stats, linked accounts) without
  touching the patch's internal classes.
- Send wire mutations (buy / sell / cancel / claim / submit / etc.)
  through stable API methods.
- Subscribe to lifecycle and cache-update events.
- Register your own wire kinds for custom client↔server messaging
  (pairs with the server-side SDK to define your own protocols).
- Show in-game toasts via the same notification helpers KMH uses.
- Resolve item labels through the same `DefDatabase` cache the
  patch uses.


## Architecture

```
        ┌──────────────────────────────────┐
        │   Your extension RimWorld mod    │
        │   (in Mods/<your-mod>/)          │
        │                                  │
        │   Source/MyExtension.cs          │
        │   1.6/Assemblies/MyExtension.dll │
        │   About/About.xml (loadAfter:    │
        │                    knappe.kmh.patch) │
        └──────────────┬───────────────────┘
                       │ stable interfaces
                       ▼
        ┌──────────────────────────────────┐
        │   KMH.Sdk.Client                 │
        │   • IKmhClientExtension          │
        │   • IKmhClientHost               │
        │   • ITreasuryCache, IQuestCache, │
        │     IMarketplaceCache, ...       │
        │   • Records, Events              │
        └──────────────┬───────────────────┘
                       │ implementations
                       ▼
        ┌──────────────────────────────────┐
        │   KMHPatch (internal)            │
        │   • TreasuryCache, QuestCache    │
        │   • Dialogs, KmhDispatcher       │
        │   • Harmony patches              │
        │   • RWT / RimWorld type plumbing │
        └──────────────────────────────────┘
```

Your extension only ever touches the top two boxes.


## Quick start

### 1. Copy the example template

```
cp -r KMH-Patch/Templates/ClientExtension/ ./my-mod
cd my-mod
```

The example is a fully working RimWorld mod that toasts the player
when their marketplace listing count goes up.

### 2. Edit About/About.xml

- Change `packageId` to your own reverse-DNS string
- Update `name` / `author` / `description`
- Keep the `<modDependencies>` + `<loadAfter>` entries for KMHPatch
- Update `<supportedVersions>` for the RimWorld versions you've
  tested

### 3. Build

```
dotnet build -c Debug
```

Output: `1.6/Assemblies/MyMod.dll`

### 4. Deploy

Copy the entire mod folder into RimWorld's Mods folder, or upload
as a separate Steam Workshop item.

In-game: enable KMH Patch *first*, then your extension. Restart
RimWorld.

You'll see in the KMH log:

```
[KMH] [ext:My KMH Accent] Registered. Will cheer on new listings.
[KMH] Extensions: scanned 1 candidate assembly/ies, loaded 1.
[KMH] Bootstrap complete - N method(s) patched, 1 extension(s) loaded
```


## SDK reference

### `IKmhClientExtension`

Implement this. The loader requires:
- A parameterless public constructor
- `Name` (display string)
- `Version` (semver string)
- `Register(IKmhClientHost)` - called once at mod init
- `Shutdown()` - called on RimWorld quit (best-effort)

### `IKmhClientHost`

The stable surface you get in `Register`. Holds:

- **Identity**: `KmhVersion`, `SdkVersion`, `KmhHarmonyId`,
  `IsKmhServer`, `LocalUsername`
- **Caches**: `Treasury`, `Marketplace`, `Quests`, `Guild`,
  `GuildLeaderboard`, `PlayerStats`, `LinkedAccounts`, `ItemLabels`
- **Helpers**: `Log` (prefixed with your extension name),
  `Toast` (in-game notifications), `Events`
- **Protocol**: `Send(kind, data)`, `RegisterHandler(kind, handler)`

### Cache APIs (`KMH.Sdk.Client.Apis.*`)

| Interface | Surface |
|-----------|---------|
| `ITreasuryCache` | `HasSnapshot`, `Snapshot`, `LastUpdatedUtc`, `RequestRefresh()` |
| `IMarketplaceCache` | `HasSnapshot`, `Listings`, `LastUpdatedUtc`, `RequestRefresh()`, `TryBuy`, `TryCancel`, `TryPost` |
| `IQuestCache` | `HasSnapshot`, `Quests`, `LastUpdatedUtc`, `RequestRefresh()`, `TryClaim`, `TrySubmit`, `TryApprove`, `TryCancel`, `TryPostDeliverItem`, `TryPostBounty` |
| `IGuildCache` | `HasSnapshot`, `InGuild`, `Snapshot`, `LastUpdatedUtc`, `RequestRefresh()` |
| `IGuildLeaderboardCache` | `HasSnapshot`, `Guilds`, `LastUpdatedUtc`, `RequestRefresh()` |
| `IPlayerStatsCache` | `HasSnapshot`, `Entries`, `LastUpdatedUtc`, `RequestRefresh()` |
| `ILinkedAccountsCache` | `HasSnapshot`, `IsLinked`, `DiscordDisplayFor`, `FormatUsername`, `LastUpdatedUtc` |
| `IItemLabelResolver` | `LabelFor`, `ResolveStuffedLabel` |

All snapshot/list reads return immutable record DTOs from
`KMH.Sdk.Client.Records.*`. Mutations are fire-and-forget - the
server responds with a fresh snapshot which triggers the
corresponding `Cache*Updated` event.

### Events (`IKmhClientEvents`)

```csharp
event Action<KmhServerConnectedEvent>           KmhServerConnected;
event Action<KmhServerDisconnectedEvent>        KmhServerDisconnected;
event Action<TreasuryCacheUpdatedEvent>         TreasuryCacheUpdated;
event Action<MarketplaceCacheUpdatedEvent>      MarketplaceCacheUpdated;
event Action<QuestCacheUpdatedEvent>            QuestCacheUpdated;
event Action<GuildCacheUpdatedEvent>            GuildCacheUpdated;
event Action<GuildLeaderboardCacheUpdatedEvent> GuildLeaderboardCacheUpdated;
event Action<PlayerStatsCacheUpdatedEvent>      PlayerStatsCacheUpdated;
event Action<LinkedAccountsCacheUpdatedEvent>   LinkedAccountsCacheUpdated;
```

Handlers fire on RimWorld's main thread (snapshot callbacks arrive
via the patch's main-thread queue). Long-running work should queue
to your own background worker.

**Subscriber-throw protection** is identical to the server side -
one bad handler can't break other extensions or KMH itself.

### Custom wire kinds

```csharp
host.RegisterHandler("myname.auction.snapshot", env =>
{
    int round = env.GetInt("round_number");
    long lot  = env.GetInt("lot_id");
    // update your in-mod UI...
});

// Send a request to the server
host.Send("myname.auction.bid", new { lot_id = 42, amount = 250 });
```

Namespace your kinds so they don't collide with KMH's `kmh.*` or
other extensions.

### Toasts

```csharp
host.Toast.Positive("Trade succeeded!");   // green
host.Toast.Rejected("Insufficient silver."); // yellow
host.Toast.Neutral("Server sent a snapshot.");  // grey
```


## License - same as server side

KMH's LICENSE Section 7 permits SDK-using extensions on both sides.
You can write extensions under any license, sell them, keep them
closed-source. You cannot fork KMH source or bundle KMH binaries
inside an extension distribution.

See `KMH-Server-Addon/LICENSE` Section 7 for the full carve-out.


## Pairs nicely with the server SDK

The two SDKs are designed to be used together. A typical
"full-stack" KMH extension is two mods:

1. **Server-side mod**: a .dll dropped in `kmh-extensions/` that
   adds custom wire kinds and persists state in
   `kmh-data/extensions/<name>/`.
2. **Client-side mod**: a RimWorld mod that subscribes to the
   server's custom wire kinds and shows UI.

Use the same namespace prefix on both sides (e.g.
`myname.auction.*`) and the same `KMH.Sdk.*` record shapes for
your data DTOs to keep the two halves in lockstep.
