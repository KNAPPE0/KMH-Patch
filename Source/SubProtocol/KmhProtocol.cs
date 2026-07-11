namespace KMHPatch.SubProtocol
{
    // KMH sub-protocol constants (rides on RWT's chat packet). The zero-width-space prefix on system usernames is
    // collision-proof: no legitimate player name can start with a non-printable control character.
    internal static class KmhProtocol
    {
        // Bump when the wire format changes incompatibly. Server announces its supported version in kmh.hello;
        // clients refuse to send if mismatched. Bumped to 2 for v1.2.0 (deposit semantics changed - gate old clients).
        public const int CurrentVersion = 2;

        // Human-readable release version, carried in kmh.hello purely so each side can DETECT a version gap and
        // nudge the player. It never gates the connection (that's CurrentVersion's job) and stays additive: a
        // pre-1.1.0 server omits it, so an empty value received here reliably means "older server".
        public const string BuildVersion = "1.2.0";

        // Short build tag bumped each dev pass so a running player can confirm WHICH client build is loaded (a stale
        // installed DLL is the #1 "my fix isn't showing" cause). Shown in the KMH tab + Guild Hall title + logged on load.
        public const string UiBuildTag = "rel-129";

        // Identifiers stamped into PKT_Chat.Username. The chat handler intercept matches on these to recognize KMH
        // protocol traffic
        public const string SystemUsername = "​[KMH-SYS]"; // server -> client
        public const string ClientUsername = "​[KMH-CLI]"; // client -> server

        // Well-known envelope kinds. Add more as features land; the dispatcher doesn't enforce these are the only
        // valid kinds - they're just shared constants so server and patch agree on the wire vocabulary
        public static class Kind
        {
            // Connection lifecycle
            public const string Hello       = "kmh.hello";       // server announces KMH support + version
            public const string HelloAck    = "kmh.hello.ack";   // client confirms compatible version

            // Heartbeat / health
            public const string Ping        = "kmh.ping";
            public const string Pong        = "kmh.pong";

            // Transient server -> client toast. Payload { level, text } where level is positive/negative/neutral.
            // Action feedback that doesn't warrant a full snapshot
            public const string Notice      = "kmh.notice";          // server -> client

            // Batch of notices that piled up while offline; delivered once on login, shown as letters.
            public const string NotifyQueued = "kmh.notify.queued";  // server -> client

            // Player stats / leaderboard
            public const string PlayerStatsRequest  = "kmh.player_stats.request";   // client -> server
            public const string PlayerStatsSnapshot = "kmh.player_stats.snapshot";  // server -> client
            public const string ColonyReport        = "kmh.colony.report";          // client -> server (colony summary + top colonist)
            public const string ColonistRequest     = "kmh.colonist.request";       // client -> server
            public const string ColonistProfile     = "kmh.colonist.profile";       // server -> client
            public const string ColonistRosterRequest = "kmh.records.colonists.request"; // client -> server
            public const string ColonistRoster        = "kmh.records.colonists";         // server -> client
            public const string SeasonArchiveRequest  = "kmh.archive.season.request";    // client -> server
            public const string SeasonArchive         = "kmh.archive.season";            // server -> client

            // Treasury - server figures out which (guild or personal) from the authenticated caller
            public const string TreasuryRequest        = "kmh.treasury.request";          // client -> server
            public const string TreasurySnapshot       = "kmh.treasury.snapshot";         // server -> client
            // Silver mutations carry { amount: N }. Server validates against caravan / treasury balance +
            // permission, then rebroadcasts kmh.treasury.snapshot
            public const string TreasuryDepositSilver  = "kmh.treasury.deposit_silver";   // client -> server
            public const string TreasuryWithdrawSilver = "kmh.treasury.withdraw_silver";  // client -> server
            // Item mutations carry { item_def_name, qty }. Server validates caravan inventory / treasury contents +
            // permission, then rebroadcasts kmh.treasury.snapshot
            public const string TreasuryDepositItem    = "kmh.treasury.deposit_item";     // client -> server
            public const string TreasuryWithdrawItem   = "kmh.treasury.withdraw_item";    // client -> server
            public const string TreasuryGrant          = "kmh.treasury.grant";           // server -> client (materialize a confirmed withdrawal into the colony)
            public const string TreasuryDepositConfirm  = "kmh.treasury.deposit_confirm";   // client -> server (these deposit txns are now durably saved locally)
            public const string TreasuryDepositReconcile= "kmh.treasury.deposit_reconcile"; // client -> server (full set of durably-saved deposit txns, sent on connect)
            public const string TreasuryDepositPreflight= "kmh.treasury.deposit_preflight"; // client -> server (approve BEFORE removing local goods)
            public const string TreasuryDepositApproval = "kmh.treasury.deposit_approval";  // server -> client (approve/deny + short-lived token)

            // Marketplace - global open-listings list.
            public const string MarketplaceRequest  = "kmh.marketplace.request";    // client -> server
            public const string MarketplaceSnapshot = "kmh.marketplace.snapshot";   // server -> client
            // Buy carries { listing_id, qty }. Cancel carries { listing_id }. Server validates (silver, ownership,
            // remaining qty) and rebroadcasts kmh.marketplace.snapshot
            public const string MarketplaceBuy      = "kmh.marketplace.buy";        // client -> server
            public const string MarketplaceCancel   = "kmh.marketplace.cancel";     // client -> server
            // Post carries { item_def_name, qty, unit_price_silver }. Server validates that the caller's caravan
            // actually holds the item + qty, then escrows it, creates the listing, and rebroadcasts
            public const string MarketplacePost     = "kmh.marketplace.post";       // client -> server

            // Quest board - global posted quests visible to caller (server filters guild-only quests by caller's
            // guild membership before sending)
            public const string QuestRequest        = "kmh.quest.request";          // client -> server
            public const string QuestSnapshot       = "kmh.quest.snapshot";         // server -> client
            // Quest mutations - each envelope carries { quest_id: N }. Server applies the change, then broadcasts a
            // fresh kmh.quest.snapshot to everyone watching (no per-mutation response packet needed)
            public const string QuestClaim          = "kmh.quest.claim";            // client -> server
            public const string QuestSubmit         = "kmh.quest.submit";           // client -> server
            public const string QuestCancel         = "kmh.quest.cancel";           // client -> server
            public const string QuestApprove        = "kmh.quest.approve";          // client -> server (poster signs off on Bounty Submit)
            public const string QuestAbandon        = "kmh.quest.abandon";          // client -> server (claimer drops a claimed quest)
            public const string QuestSubmitProof    = "kmh.quest.submit_proof";     // client -> server (Custom: claimer submits proof for review)
            public const string QuestReview         = "kmh.quest.review";           // client -> server (poster approves/rejects a PendingReview)
            public const string QuestVerify         = "kmh.quest.verify";           // client -> server (auto-verify report for escort/defend/hunt/build)
            // Post creates a new quest. Carries the composer's fields - server escrows the bounty silver, assigns
            // an id + posted_utc_ticks, adds it to the board, and rebroadcasts. Kind defaults to 'deliver_item' for
            // v1; bounty kind UI lands later
            public const string QuestPost           = "kmh.quest.post";             // client -> server

            // Guild - caller's current guild snapshot (or "not in a guild" marker). Server resolves which guild
            // from the caller's membership
            public const string GuildRequest        = "kmh.guild.request";          // client -> server
            public const string GuildSnapshot       = "kmh.guild.snapshot";         // server -> client
            // Member-management mutations. Promote / Demote carry { username }; server picks the target rank from
            // the current membership state (Member <-> Officer <-> Moderator). Kick carries { username }. Server
            // validates caller's own rank + target's rank before applying, then rebroadcasts the snapshot
            public const string GuildPromote        = "kmh.guild.promote";          // client -> server
            public const string GuildDemote         = "kmh.guild.demote";           // client -> server
            public const string GuildKick           = "kmh.guild.kick";             // client -> server
            // Perk purchase. Carries { perk_key } where perk_key is one of the well-known strings below. Server
            // deducts silver from the guild treasury and bumps the level
            public const string GuildBuyPerk        = "kmh.guild.buy_perk";         // client -> server
            // MOTD. Carries { motd } (empty string clears). Admin-only.
            public const string GuildSetMotd        = "kmh.guild.set_motd";         // client -> server
            public const string GuildLeave          = "kmh.guild.leave";            // client -> server
            public const string GuildDonate         = "kmh.guild.donate";           // client -> server ({ amount })
            public const string GuildWithdraw       = "kmh.guild.withdraw";         // client -> server ({ amount }) guild vault -> personal, rank-capped
            public const string GuildTransferOwner  = "kmh.guild.transfer_owner";   // client -> server ({ username }) Owner only
            // Alliance / hostility. Each carries { other_guild }. Server validates rank (admin) + relationship
            // state and rebroadcasts. ProposeAlliance caller -> other: None -> AlliedRequested AcceptAlliance
            // caller -> other: AlliedRequested -> Allied BreakAlliance caller -> other: Allied -> None
            // DeclareHostile caller -> other: any -> Hostile ClearHostile caller -> other: Hostile -> None
            public const string GuildProposeAlliance = "kmh.guild.propose_alliance"; // client -> server
            public const string GuildAcceptAlliance  = "kmh.guild.accept_alliance";  // client -> server
            public const string GuildBreakAlliance   = "kmh.guild.break_alliance";   // client -> server
            public const string GuildDeclareHostile  = "kmh.guild.declare_hostile";  // client -> server
            public const string GuildClearHostile    = "kmh.guild.clear_hostile";    // client -> server
            // Settings save. Carries the full GuildSettingsDto shape - server validates ranges (tax% 0..50, caps in
            // [-1, MAX_INT]) and rebroadcasts. Admin-only
            public const string GuildSaveSettings    = "kmh.guild.save_settings";    // client -> server
            // Invite a player (admin/mod) { username }; toggle open-join (admin) { open }; join an open or invited
            // guild { guild }
            public const string GuildInvite          = "kmh.guild.invite";           // client -> server
            public const string GuildDeclineInvite   = "kmh.guild.decline_invite";   // client -> server (invitee turns an invite down)
            public const string GuildInvitablesRequest  = "kmh.guild.invitables.request"; // client -> server (known guildless players for the invite picker)
            public const string GuildInvitablesSnapshot = "kmh.guild.invitables.snapshot"; // server -> client
            public const string GuildSetOpenJoin     = "kmh.guild.set_open_join";    // client -> server
            public const string GuildJoin            = "kmh.guild.join";             // client -> server
            public const string GuildCreate          = "kmh.guild.create";           // client -> server
            public const string GuildHallSet         = "kmh.guild.hall.set";         // client -> server
            public const string GuildHallRemove      = "kmh.guild.hall.remove";      // client -> server

            // Linked accounts - in-game username -> Discord display name map. Server is expected to push the
            // snapshot after handshake and again on every link/unlink, so the client doesn't need to poll. Request
            // kind exists for the rare hard-refresh case
            public const string LinkedAccountsRequest  = "kmh.linked_accounts.request";  // client -> server
            public const string LinkedAccountsSnapshot = "kmh.linked_accounts.snapshot"; // server -> client
            public const string LinkRequest            = "kmh.link.request";            // client -> server
            public const string LinkCode               = "kmh.link.code";               // server -> client

            // Cross-guild leaderboard - every guild on the server in a leaderboard-friendly shape (separate from
            // GuildSnapshot which is caller-scoped to the caller's own guild)
            public const string GuildLeaderboardRequest  = "kmh.guild_leaderboard.request";  // client -> server
            public const string GuildLeaderboardSnapshot = "kmh.guild_leaderboard.snapshot"; // server -> client

            // Player reputation roster (username -> score + tier) for badges.
            public const string ReputationRequest   = "kmh.reputation.request";      // client -> server
            public const string ReputationSnapshot  = "kmh.reputation.snapshot";     // server -> client

            // World Engine - server-wide events + server-owned quests (same snapshot for everyone).
            public const string WorldRequest        = "kmh.world.request";          // client -> server
            public const string WorldSnapshot       = "kmh.world.snapshot";         // server -> client
            // Global-quest contribution report: { quest_id, total }. total is our cumulative tally (kills since we
            // first saw the quest, or our current standing build count). Server keeps the max per user
            public const string WorldContribute     = "kmh.world.contribute";       // client -> server
            // Global-quest item delivery: { quest_id, item_def_name, qty }. We remove the goods from the caravan
            // first; the server credits an active matching deliver quest and ignores anything else (no give-back)
            public const string WorldDeliver        = "kmh.world.deliver";          // client -> server

            // Marketplace auctions - timed bidding on treasury items.
            public const string AuctionRequest      = "kmh.auction.request";        // client -> server
            public const string AuctionSnapshot     = "kmh.auction.snapshot";       // server -> client
            public const string AuctionPost         = "kmh.auction.post";           // client -> server
            public const string AuctionBid          = "kmh.auction.bid";            // client -> server
            public const string AuctionCancel       = "kmh.auction.cancel";         // client -> server

            // Want-to-buy board - buyers escrow silver, sellers fulfill from treasury.
            public const string WantRequest         = "kmh.want.request";           // client -> server
            public const string WantSnapshot        = "kmh.want.snapshot";          // server -> client
            public const string WantPost            = "kmh.want.post";              // client -> server
            public const string WantFulfill         = "kmh.want.fulfill";           // client -> server
            public const string WantCancel          = "kmh.want.cancel";            // client -> server

            // KMH custom sites - player-built production nodes with workers.
            public const string SiteRequest         = "kmh.site.request";           // client -> server
            public const string SiteSnapshot        = "kmh.site.snapshot";          // server -> client
            public const string SiteBuild           = "kmh.site.build";             // client -> server
            public const string SiteJoin            = "kmh.site.join";              // client -> server
            public const string SiteLeave           = "kmh.site.leave";             // client -> server
            public const string SiteSetDestination  = "kmh.site.set_destination";   // client -> server
            public const string SiteCancel          = "kmh.site.cancel";            // client -> server
            public const string SiteCatalogRequest  = "kmh.site.catalog.request";   // client -> server (curated output picker)
            public const string SiteCatalog         = "kmh.site.catalog";           // server -> client (classified allowed outputs)

            // Client -> server defName->label map at handshake; server accumulates the union so friendly names work in
            // Discord commands/browse even for items its own (headless) build has no def for.
            public const string ItemLabels             = "kmh.item_labels";              // client -> server
            // defName -> BaseMarketValue (RimWorld's canonical prices). Separate envelope so it never bloats the
            // already near-cap labels push; lets the server value-scale quest rewards / pricing.
            public const string ItemValues             = "kmh.item_values";              // client -> server
            // GameConditionDefs (defName -> label) from our game, incl. mods - feeds the server's discovered weather.
            public const string ConditionDefs          = "kmh.condition_defs";           // client -> server

            // Batched KMH log lines to the server's Debug/ folder. Sent when the player enables it in mod options,
            // or automatically when the server asks (debug_uplink in the hello).
            public const string DebugLog               = "kmh.debug.log";                // client -> server

            // Config enforcement. The server pushes the enforcement snapshot (on/off + admin-bypass + safe-mods
            // allowlist) on handshake and on change; the patch locks the Mod Options screen for non-admins
            // accordingly. The full config profile (hard enforcement) rides on the chunked profile.* kinds, and
            // restore clears it
            public const string EnforcementSnapshot     = "kmh.enforcement.snapshot";      // server -> client
            public const string EnforcementSnapshotRequest = "kmh.enforcement.snapshot.request"; // client -> server (refresh on dialog open, e.g. after being op'd mid-session)
            public const string EnforcementProfileRequest = "kmh.enforcement.profile.request"; // client -> server (only when the local hash differs)
            public const string EnforcementProfileBegin = "kmh.enforcement.profile.begin"; // server -> client (chunked profile header)
            public const string EnforcementProfileChunk = "kmh.enforcement.profile.chunk"; // server -> client
            public const string EnforcementProfileEnd   = "kmh.enforcement.profile.end";   // server -> client
            public const string EnforcementRestore      = "kmh.enforcement.restore";       // server -> client (lift enforcement, restore personal configs)
            public const string EnforcementSetEnabled   = "kmh.enforcement.set_enabled";   // client -> server (admin toggles enforcement)
            public const string EnforcementSetSafe      = "kmh.enforcement.set_safe";      // client -> server (admin adds/removes a safe mod)
            public const string EnforcementSetFlag      = "kmh.enforcement.set_flag";      // client -> server (admin toggles admin_bypass / preserve_personal)
            public const string EnforcementUploadBegin  = "kmh.enforcement.upload.begin";  // client -> server (admin publishes their configs as the profile)
            public const string EnforcementUploadChunk  = "kmh.enforcement.upload.chunk";  // client -> server
            public const string EnforcementUploadEnd    = "kmh.enforcement.upload.end";    // client -> server
        }
    }
}
