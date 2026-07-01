using KMHPatch.Diagnostics;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Guilds
{
    // Registers the guild sub-protocol handler + exposes the full set of mutations the Guild Hall dialog calls:
    // member-management (promote / demote / kick), perk purchase, MOTD edit, settings save, and the alliance /
    // hostility lifecycle (propose / accept / break / declare / clear). Each is a thin wire wrapper - server
    // applies and broadcasts a fresh guild snapshot
    //
    // Cross-guild leaderboard request lives here too - RequestLeaderboard / OnLeaderboardSnapshot populate
    // GuildLeaderboardCache
    internal static class GuildHandler
    {
        // Perk keys - must stay in lockstep with what the server's GuildBuyPerk handler accepts
        public const string PerkSiteMaxWorkers      = "site_max_workers";
        public const string PerkMarketplaceTaxCut   = "marketplace_tax_cut";
        public const string PerkWorkerXpBonus       = "worker_xp_bonus";
        public const string PerkCustomSiteCostCut   = "custom_site_cost_cut";

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.GuildSnapshot,            OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.GuildLeaderboardSnapshot, OnLeaderboardSnapshot);
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

        // Invite / open-join / join. The server replies with a kmh.notice toast (success, or the specific reason it
        // failed), so these don't flash an optimistic message - they only report a local send failure
        public static bool TryInvite(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                KmhNotifications.Rejected("Enter a player name to invite");
                return false;
            }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildInvite, new { username = username });
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TrySetOpenJoin(bool open)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildSetOpenJoin, new { open = open });
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TryJoin(string guildName)
        {
            if (string.IsNullOrWhiteSpace(guildName))
            {
                KmhNotifications.Rejected("Enter a guild name to join");
                return false;
            }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildJoin, new { guild = guildName });
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool CreateGuild(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                KmhNotifications.Rejected("Enter a guild name");
                return false;
            }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildCreate, new { name = name });
            if (sent) KmhNotifications.Positive($"Creating guild {name}…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TryBuyPerk(string perkKey)
        {
            if (string.IsNullOrEmpty(perkKey))
            {
                KmhNotifications.Rejected("Unknown perk");
                return false;
            }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildBuyPerk, new { perk_key = perkKey });
            if (sent) KmhNotifications.Positive($"Buying perk: {perkKey}");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // motd may be empty (clears the existing message). Server enforces admin-only and a max length cap
        public static bool TrySetMotd(string motd)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildSetMotd, new { motd = motd ?? "" });
            if (sent) KmhNotifications.Positive("MOTD update sent");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Five alliance/hostility mutations all share the same wire shape ({ other_guild }) so they collapse to one
        // helper
        public static bool TryProposeAlliance(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildProposeAlliance, otherGuild, $"Alliance proposed to {otherGuild}");
        public static bool TryAcceptAlliance(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildAcceptAlliance,  otherGuild, $"Alliance with {otherGuild} accepted");
        public static bool TryBreakAlliance(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildBreakAlliance,   otherGuild, $"Alliance with {otherGuild} broken");
        public static bool TryDeclareHostile(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildDeclareHostile,  otherGuild, $"Declared hostile to {otherGuild}");
        public static bool TryClearHostile(string otherGuild)
            => SendAllianceAction(KmhProtocol.Kind.GuildClearHostile,    otherGuild, $"Hostility with {otherGuild} cleared");

        public static bool TrySaveSettings(GuildSettingsDto settings)
        {
            if (settings == null)
            {
                KmhNotifications.Rejected("Settings payload is empty");
                return false;
            }
            // Server is the authoritative validator. We just send the typed DTO directly - Newtonsoft serializes
            // its JsonProperty names into the envelope's data field, so the wire shape matches the settings
            // sub-object of kmh.guild.snapshot byte-for-byte
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.GuildSaveSettings, settings);
            if (sent) KmhNotifications.Positive("Settings save sent");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
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
            else      KmhNotifications.Rejected("Not connected to a KMH server");
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
            else      KmhNotifications.Rejected("Not connected to a KMH server");
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
