using KMHPatch.Diagnostics;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Guilds
{
    internal static class GuildHandler
    {
        // In lockstep with what the server's GuildBuyPerk handler accepts.
        public const string PerkSiteMaxWorkers      = "site_max_workers";
        public const string PerkMarketplaceTaxCut   = "marketplace_tax_cut";
        public const string PerkWorkerXpBonus       = "worker_xp_bonus";
        public const string PerkCustomSiteCostCut   = "custom_site_cost_cut";

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.GuildSnapshot,            OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.GuildLeaderboardSnapshot, OnLeaderboardSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.GuildInvitablesSnapshot,  OnInvitablesSnapshot);
        }

        public static System.Collections.Generic.List<InvitablePlayerDto> Invitables { get; private set; }
            = new System.Collections.Generic.List<InvitablePlayerDto>();

        public static bool RequestInvitables()
            => KmhDispatcher.Send(KmhProtocol.Kind.GuildInvitablesRequest, null);

        private static void OnInvitablesSnapshot(KmhEnvelope env)
        {
            GuildInvitablesSnapshot snap = env?.DataAs<GuildInvitablesSnapshot>();
            if (snap?.Players != null) Invitables = snap.Players;
        }

        public static bool RequestSnapshot()
        {
            return KmhDispatcher.Send(KmhProtocol.Kind.GuildRequest, null);
        }

        public static bool RequestLeaderboard()
        {
            return KmhDispatcher.Send(KmhProtocol.Kind.GuildLeaderboardRequest, null);
        }

        public static bool TryPromote(string username)
        {
            return SendMemberAction(KmhProtocol.Kind.GuildPromote, username, $"Promoting {username}");
        }

        public static bool TryDemote(string username)
        {
            return SendMemberAction(KmhProtocol.Kind.GuildDemote, username, $"Demoting {username}");
        }

        public static bool TryKick(string username)
        {
            return SendMemberAction(KmhProtocol.Kind.GuildKick, username, $"Kicking {username}");
        }

        // The server replies with its own notice, so this flashes nothing optimistic and reports only a send failure.
        public static bool TryInvite(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                KmhNotifications.Rejected("Enter a player name to invite");
                return false;
            }
            return Send(KmhProtocol.Kind.GuildInvite,
                Treasury.EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "username", username } }));
        }

        public static bool TrySetHall()
        {
            int tile = Treasury.EconomyCtx.CurrentTile();
            if (tile < 0) { KmhNotifications.Rejected("No colony or caravan to place the Guild Hall at"); return false; }
            return Send(KmhProtocol.Kind.GuildHallSet, new { tile = tile });
        }

        public static bool TrySetHallAt(int tile)
        {
            if (tile < 0) { KmhNotifications.Rejected("Pick a valid world tile for the Guild Hall"); return false; }
            return Send(KmhProtocol.Kind.GuildHallSet, new { tile = tile });
        }

        public static bool TryRemoveHall() => Send(KmhProtocol.Kind.GuildHallRemove, null);

        public static bool TryDeclineInvite(string guildName)
        {
            if (string.IsNullOrWhiteSpace(guildName)) return false;
            return Send(KmhProtocol.Kind.GuildDeclineInvite, new { guild = guildName });
        }

        public static bool TrySetOpenJoin(bool open) => Send(KmhProtocol.Kind.GuildSetOpenJoin, new { open = open });

        public static bool TryJoin(string guildName)
        {
            if (string.IsNullOrWhiteSpace(guildName))
            {
                KmhNotifications.Rejected("Enter a guild name to join");
                return false;
            }
            return Send(KmhProtocol.Kind.GuildJoin,
                Treasury.EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object> { { "guild", guildName } }));
        }

        public static bool CreateGuild(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                KmhNotifications.Rejected("Enter a guild name");
                return false;
            }
            // The server reads the tile only when the create-needs-a-hall rule is on, and ignores it otherwise.
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildCreate, new { name = name, hall_tile = Treasury.EconomyCtx.CurrentTile() });
            if (sent) KmhNotifications.Positive($"Creating guild {UI.KmhDisplayText.GuildName(name)}…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryBuyPerk(string perkKey)
        {
            if (string.IsNullOrEmpty(perkKey))
            {
                KmhNotifications.Rejected("Unknown perk");
                return false;
            }
            // A buy can fail on rank, funds or a maxed perk, so reporting success on send would be a lie.
            return Send(KmhProtocol.Kind.GuildBuyPerk, new { perk_key = perkKey },
                SubProtocol.KmhOpId.For($"guild.buy_perk|{perkKey}"));
        }

        // The server enforces the last-admin block and disband/vault-return, and answers with an authoritative notice.
        public static bool TryLeave() => Send(KmhProtocol.Kind.GuildLeave, null);

        // The op id doubles as the pending txn id, so it is minted here rather than left to the transport.
        public static bool TryDonate(int amount)
        {
            if (amount <= 0) { KmhNotifications.Rejected("Enter a positive amount"); return false; }
            string key = $"guild.donate|{amount}";
            if (SubProtocol.KmhOpId.IsInFlight(key))
            { KmhNotifications.Neutral("Donation already sent - waiting for the server."); return false; }
            string opId = SubProtocol.KmhOpId.For(key);
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildDonate,
                Treasury.EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object>
                    { { "amount", amount }, { "req_id", opId } }), opId);
            if (sent) Treasury.GameComponent_KMHDepositLedger.Instance?.RecordDeposit(opId);
            else { SubProtocol.KmhOpId.Settled(key); KmhNotifications.NotConnected(); }
            return sent;
        }

        public static bool TryWithdrawFromGuild(int amount)
        {
            if (amount <= 0) { KmhNotifications.Rejected("Enter a positive amount"); return false; }
            string key = $"guild.withdraw|{amount}";
            if (SubProtocol.KmhOpId.IsInFlight(key))
            { KmhNotifications.Neutral("Withdraw already sent - waiting for the server."); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildWithdraw,
                Treasury.EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object>
                    { { "amount", amount } }), SubProtocol.KmhOpId.For(key));
            if (!sent) { SubProtocol.KmhOpId.Settled(key); KmhNotifications.NotConnected(); }
            return sent;
        }

        // The goods are delivered to the colony, like a treasury withdrawal; the server checks rank, cap and stock.
        public static bool TryWithdrawGuildItem(string itemKey, int qty)
            => SendGuildItemWithdraw("item_def_name", itemKey, qty);

        public static bool TryWithdrawGuildPayload(string fingerprint, int qty)
            => SendGuildItemWithdraw("fingerprint", fingerprint, qty);

        private static bool SendGuildItemWithdraw(string field, string value, int qty)
        {
            if (string.IsNullOrEmpty(value)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            string key = $"guild.withdraw_item|{value}|{qty}";
            if (SubProtocol.KmhOpId.IsInFlight(key))
            { KmhNotifications.Neutral("Withdraw already sent - waiting for the server."); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildWithdrawItem,
                Treasury.EconomyCtx.With(new System.Collections.Generic.Dictionary<string, object>
                    { { field, value }, { "qty", qty } }), SubProtocol.KmhOpId.For(key));
            if (!sent) { SubProtocol.KmhOpId.Settled(key); KmhNotifications.NotConnected(); }
            return sent;
        }

        public static bool TryTransferOwner(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;
            return Send(KmhProtocol.Kind.GuildTransferOwner, new { username = username });
        }

        public static bool TrySetMotd(string motd)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildSetMotd, new { motd = motd ?? "" });
            if (sent) KmhNotifications.Positive("MOTD update sent");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryProposeAlliance(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildProposeAlliance, otherGuild, $"Alliance proposed to {UI.KmhDisplayText.GuildName(otherGuild)}");
        public static bool TryAcceptAlliance(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildAcceptAlliance,  otherGuild, $"Alliance with {UI.KmhDisplayText.GuildName(otherGuild)} accepted");
        public static bool TryBreakAlliance(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildBreakAlliance,   otherGuild, $"Alliance with {UI.KmhDisplayText.GuildName(otherGuild)} broken");
        // Break also answers a proposal: the server clears whichever side is open, ours or theirs.
        public static bool TryDeclineAlliance(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildBreakAlliance,   otherGuild,
                                  UI.KmhText.Get("KMH_Guild_FlashDeclined", "Declined the alliance from {0}", UI.KmhDisplayText.GuildName(otherGuild)));
        public static bool TryCancelProposal(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildBreakAlliance,   otherGuild,
                                  UI.KmhText.Get("KMH_Guild_FlashCancelled", "Proposal to {0} cancelled", UI.KmhDisplayText.GuildName(otherGuild)));
        public static bool TryDeclareHostile(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildDeclareHostile,  otherGuild,
                                  UI.KmhText.Get("KMH_Guild_FlashRival", "Marked {0} as a rival", UI.KmhDisplayText.GuildName(otherGuild)));
        public static bool TryClearHostile(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildClearHostile,    otherGuild,
                                  UI.KmhText.Get("KMH_Guild_FlashRivalCleared", "Rival mark on {0} cleared", UI.KmhDisplayText.GuildName(otherGuild)));

        public static bool TrySaveSettings(GuildSettingsDto settings)
        {
            if (settings == null)
            {
                KmhNotifications.Rejected("Settings payload is empty");
                return false;
            }
            // Sent as the typed DTO, so the wire shape matches the snapshot's settings sub-object byte for byte.
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildSaveSettings, settings);
            if (sent) KmhNotifications.Positive("Settings save sent");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        private static bool Send(string kind, object data, string opId = null)
        {
            bool sent = KmhDispatcher.Send(kind, data, opId);
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        private static bool SendAllianceAction(string kind, string otherGuild, string flashOnSent)
        {
            if (string.IsNullOrWhiteSpace(otherGuild))
            {
                KmhNotifications.Rejected("Other guild name is required");
                return false;
            }
            bool sent = KmhDispatcher.Send(kind, new { other_guild = otherGuild });
            if (sent) KmhNotifications.Positive(flashOnSent);
            else      KmhNotifications.NotConnected();
            return sent;
        }

        private static bool SendMemberAction(string kind, string username, string flashOnSent)
        {
            if (string.IsNullOrEmpty(username))
            {
                KmhNotifications.Rejected("Username is empty");
                return false;
            }
            bool sent = KmhDispatcher.Send(kind, new { username = username });
            if (sent) KmhNotifications.Positive(flashOnSent);
            else      KmhNotifications.NotConnected();
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            GuildSnapshotEnvelope snapshot = env?.DataAs<GuildSnapshotEnvelope>();
            if (snapshot == null)
            {
                KmhLog.Warn("Guild snapshot envelope had no parseable payload, ignoring");
                return;
            }
            GuildCache.Apply(snapshot);
        }

        private static void OnLeaderboardSnapshot(KmhEnvelope env)
        {
            GuildLeaderboardSnapshot snapshot = env?.DataAs<GuildLeaderboardSnapshot>();
            if (snapshot == null)
            {
                KmhLog.Warn("Guild leaderboard snapshot had no parseable payload, ignoring");
                return;
            }
            GuildLeaderboardCache.Apply(snapshot);
        }
    }
}
