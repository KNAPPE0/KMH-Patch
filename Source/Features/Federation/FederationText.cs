using System;
using System.Globalization;
using KMHPatch.UI;

namespace KMHPatch.Features.Federation
{
    // The contract's vocabularies in the player's words. A value this build does not know is shown as itself.
    internal static class FederationText
    {
        public static string Unavailable(string code)
        {
            switch (code)
            {
                case "feature.off":        return KmhText.Get("KMH_Fed_UnavailFeatureOff", "Guild Federation is switched off on this server.");
                case "global.off":         return KmhText.Get("KMH_Fed_UnavailGlobalOff", "This server's owner has switched Colony Nexus Global off, so Guild Federation cannot reach it.");
                case "global.setup":       return KmhText.Get("KMH_Fed_UnavailGlobalSetup", "This server has no Colony Nexus Global identity yet. It sets one up on its own, so try again in a few minutes.");
                case "global.unreachable": return KmhText.Get("KMH_Fed_UnavailUnreachable", "Colony Nexus Global is not answering this server right now.");
                case "global.unsupported": return KmhText.Get("KMH_Fed_UnavailUnsupported", "The Colony Nexus Global this server uses is too old for Guild Federation.");
                case "global.revoked":     return KmhText.Get("KMH_Fed_UnavailRevoked", "This server's Colony Nexus Global registration was revoked, so Guild Federation is not available here.");
                default:                   return code ?? "";
            }
        }

        // An unknown or missing code keeps the server's own English sentence, which is all an older server sends.
        public static string Why(string code, string serverText)
        {
            switch (code ?? "")
            {
                case "account.signed_out":     return KmhText.Get("KMH_Fed_WhySignedOut", "Sign in to Colony Nexus Global with Steam under Account & links. This server confirms who you are as soon as you do.");
                case "account.local_address":  return KmhText.Get("KMH_Fed_WhyLocalAddress", "You are signed in to Colony Nexus Global, but this connection is unverified: you joined through a local or private address, and Global only confirms players who join through the server's public address.");
                case "account.other_address":  return KmhText.Get("KMH_Fed_WhyOtherAddress", "You are signed in to Colony Nexus Global, but this connection is unverified: you joined through an address that is not the one this server registered with Global.");
                case "account.unconfirmed":    return KmhText.Get("KMH_Fed_WhyUnconfirmed", "You are signed in to Colony Nexus Global, but it has not confirmed this connection yet. Colony Nexus keeps trying on its own.");
                case "account.ticket_refused": return KmhText.Get("KMH_Fed_WhyTicketRefused", "You are signed in to Colony Nexus Global, but this server could not accept your identity ticket. Colony Nexus keeps trying on its own.");
                case "account.unknown":        return KmhText.Get("KMH_Fed_WhyUnknown", "This server has no proof of your Colony Nexus Global identity on this connection yet. Sign in with Steam under Account & links if you have not.");
                case "global.identity":        return KmhText.Get("KMH_Fed_WhyServerIdentity", "This server is still getting its Colony Nexus Global identity, so it cannot confirm who you are yet.");
                case "guild.none":             return KmhText.Get("KMH_Fed_WhyNoGuild", "You are not in a guild.");
                case "guild.missing":          return KmhText.Get("KMH_Fed_WhyGuildMissing", "Your guild record is missing.");
                case "rank.manage":            return KmhText.Get("KMH_Fed_WhyRankManage", "Only a guild Admin or the Owner can manage the guild's federation.");
                case "rank.lead":              return KmhText.Get("KMH_Fed_WhyRankLead", "Only the guild Owner can lead the federation.");
                case "federation.not_leader":  return KmhText.Get("KMH_Fed_WhyNotLeader", "Only the federation's leading guild can do this.");
                case "feature.off":
                case "global.off":
                case "global.setup":
                case "global.unreachable":
                case "global.unsupported":
                case "global.revoked":         return Unavailable(code);
                default:                       return serverText ?? "";
            }
        }

        public static string LinkState(string state)
        {
            switch (state)
            {
                case "Active":          return KmhText.Get("KMH_Fed_LinkActive", "Active");
                case "ReproofRequired": return KmhText.Get("KMH_Fed_LinkReproof", "Needs to be proved again");
                case "Revoked":         return KmhText.Get("KMH_Fed_LinkRevoked", "Revoked");
                case "Left":            return KmhText.Get("KMH_Fed_LinkLeft", "Left");
                case "Removed":         return KmhText.Get("KMH_Fed_LinkRemoved", "Removed");
                case "Disbanded":       return KmhText.Get("KMH_Fed_LinkDisbanded", "Disbanded");
                default:                return state ?? "";
            }
        }

        public static string FederationState(string state)
        {
            switch (state)
            {
                case "Active":    return KmhText.Get("KMH_Fed_StateActive", "Active");
                case "Disbanded": return KmhText.Get("KMH_Fed_StateDisbanded", "Disbanded");
                default:          return state ?? "";
            }
        }

        public static string ObjectiveState(string state)
        {
            switch (state)
            {
                case "Active":    return KmhText.Get("KMH_Fed_ObjActive", "In progress");
                case "Completed": return KmhText.Get("KMH_Fed_ObjCompleted", "Completed");
                case "Expired":   return KmhText.Get("KMH_Fed_ObjExpired", "Expired");
                default:          return state ?? "";
            }
        }

        public static string Category(string category)
        {
            switch (category)
            {
                case "quest":    return KmhText.Get("KMH_Fed_CatQuest", "Player quests");
                case "world":    return KmhText.Get("KMH_Fed_CatWorld", "Living World quests");
                case "site":     return KmhText.Get("KMH_Fed_CatSite", "Site production");
                case "road":     return KmhText.Get("KMH_Fed_CatRoad", "Road building");
                case "frontier": return KmhText.Get("KMH_Fed_CatFrontier", "Frontier outposts");
                case "activity": return KmhText.Get("KMH_Fed_CatActivity", "Active members");
                default:         return category ?? "";
            }
        }

        public static string EventKind(string kind)
        {
            switch (kind)
            {
                case "Created":             return KmhText.Get("KMH_Fed_EvCreated", "Founded");
                case "Joined":              return KmhText.Get("KMH_Fed_EvJoined", "Joined");
                case "Left":                return KmhText.Get("KMH_Fed_EvLeft", "Left");
                case "Removed":             return KmhText.Get("KMH_Fed_EvRemoved", "Removed");
                case "LeaderTransferred":   return KmhText.Get("KMH_Fed_EvLeaderTransferred", "Leadership handed over");
                case "LeaderSucceeded":     return KmhText.Get("KMH_Fed_EvLeaderSucceeded", "Leadership passed on");
                case "ProfileUpdated":      return KmhText.Get("KMH_Fed_EvProfileUpdated", "Profile updated");
                case "InviteIssued":        return KmhText.Get("KMH_Fed_EvInviteIssued", "Invite issued");
                case "InviteRevoked":       return KmhText.Get("KMH_Fed_EvInviteRevoked", "Invite withdrawn");
                case "ObjectiveCompleted":  return KmhText.Get("KMH_Fed_EvObjectiveCompleted", "Objective completed");
                case "LevelReached":        return KmhText.Get("KMH_Fed_EvLevelReached", "Level reached");
                case "AchievementUnlocked": return KmhText.Get("KMH_Fed_EvAchievementUnlocked", "Achievement unlocked");
                case "Disbanded":           return KmhText.Get("KMH_Fed_EvDisbanded", "Disbanded");
                case "Reproved":            return KmhText.Get("KMH_Fed_EvReproved", "Link proved again");
                default:                    return kind ?? "";
            }
        }

        // Most kinds carry a vocabulary value as their detail; Created carries the federation's name as typed.
        public static string EventDetail(string kind, string detail)
        {
            detail = detail ?? "";
            switch (kind)
            {
                case "ObjectiveCompleted":
                {
                    int colon = detail.LastIndexOf(':');
                    if (colon <= 0) return Category(detail);
                    return KmhText.Get("KMH_Fed_DetailObjective", "{0}, target {1}",
                                       Category(detail.Substring(0, colon)), detail.Substring(colon + 1));
                }
                case "LevelReached":        return KmhText.Get("KMH_Fed_DetailLevel", "Level {0}", detail);
                case "AchievementUnlocked": return Achievement(detail);
                case "Left":
                case "Disbanded":           return Reason(detail);
                case "ProfileUpdated":      return ProfileFields(detail);
                case "InviteIssued":
                case "InviteRevoked":
                    return detail.Length == 0 ? "" : KmhText.Get("KMH_Fed_DetailInviteServer", "for server {0}",
                                                                 detail.Length > 8 ? detail.Substring(0, 8) : detail);
                default:                    return detail;
            }
        }

        // Known departure codes in the player's words; anything else is a leader's own disband reason, shown as written.
        public static string Reason(string raw)
        {
            raw = raw ?? "";
            switch (raw)
            {
                case "":
                    return "";
                case "guild_disbanded":
                    return KmhText.Get("KMH_Fed_ReasonGuildDisbanded", "the guild was disbanded on its own server");
                case "unlinked":
                    return KmhText.Get("KMH_Fed_ReasonUnlinked", "its server took the guild out of the federation");
                case "removed":
                    return KmhText.Get("KMH_Fed_ReasonRemoved", "the federation's leader removed it");
                case "federation disbanded":
                    return KmhText.Get("KMH_Fed_ReasonFederationDisbanded", "the federation was disbanded");
                case "last member left":
                    return KmhText.Get("KMH_Fed_ReasonLastMemberLeft", "its last member left");
                case "no active members at upgrade":
                    return KmhText.Get("KMH_Fed_ReasonNoMembersAtUpgrade", "it had no active members when federations were introduced");
                case "identity_proven":
                    return KmhText.Get("KMH_Fed_ReasonIdentityProven", "the server proved its own identity, so what another key made under it ended");
                case "identity_rebound":
                    return KmhText.Get("KMH_Fed_ReasonIdentityRebound", "the Colony Nexus Global operator returned the server's identity to its rightful key");
                case "server_revoked":
                    return KmhText.Get("KMH_Fed_ReasonServerRevoked", "the server's Colony Nexus Global registration was revoked");
                case "generation_changed":
                case "signing generation changed":
                    return KmhText.Get("KMH_Fed_ReasonGenerationChanged", "the server's signing key changed, so the link must be proved again");
                case "reproof_required":
                    return KmhText.Get("KMH_Fed_ReasonReproofRequired", "Colony Nexus Global asks for the link to be proved again");
                case "link_unknown":
                    return KmhText.Get("KMH_Fed_ReasonLinkUnknown", "Colony Nexus Global has no record of this link any more");
                case "data_set_changed":
                    return KmhText.Get("KMH_Fed_ReasonDataSetChanged", "this server's data was restored or moved, so the link must be proved again");
            }
            if (Actor(raw, "left_by:", out string who))
                return KmhText.Get("KMH_Fed_ReasonLeftBy", "{0} took the guild out of the federation", who);
            if (Actor(raw, "disbanded_by:", out who))
                return KmhText.Get("KMH_Fed_ReasonDisbandedBy", "{0} disbanded the federation", who);
            return raw;
        }

        // A link's own detail also comes from servers released before the codes, which wrote sentences instead.
        public static string LinkDetail(string raw)
        {
            raw = raw ?? "";
            switch (raw)
            {
                case "the guild was disbanded":                                            return Reason("guild_disbanded");
                case "this server's signing generation changed since the link was proved": return Reason("generation_changed");
                case "Colony Nexus Global requires this link to be proved again":          return Reason("reproof_required");
                case "Colony Nexus Global no longer knows this link":                      return Reason("link_unknown");
                case "this data set identity changed since the link was proved":           return Reason("data_set_changed");
            }
            if (Actor(raw, "left by ", out string who)) return Reason("left_by:" + who);
            if (Actor(raw, "disbanded by ", out who)) return Reason("disbanded_by:" + who);
            return Reason(raw);
        }

        private static bool Actor(string raw, string prefix, out string actor)
        {
            actor = raw.StartsWith(prefix, StringComparison.Ordinal) ? raw.Substring(prefix.Length).Trim() : "";
            return actor.Length > 0;
        }

        private static string ProfileFields(string detail)
        {
            var words = new System.Collections.Generic.List<string>();
            foreach (string field in (detail ?? "").Split(','))
            {
                switch (field.Trim())
                {
                    case "":            break;
                    case "name":        words.Add(KmhText.Get("KMH_Fed_FieldName", "name")); break;
                    case "tag":         words.Add(KmhText.Get("KMH_Fed_FieldTag", "tag")); break;
                    case "description": words.Add(KmhText.Get("KMH_Fed_FieldDescription", "description")); break;
                    case "color":       words.Add(KmhText.Get("KMH_Fed_FieldColor", "colour")); break;
                    default:            words.Add(field.Trim()); break;
                }
            }
            return string.Join(", ", words);
        }

        // Every achievement the contract names, so the window can show the ones still to earn as well.
        public static readonly string[] AchievementKeys =
        {
            "founded", "first_ally", "five_servers", "level_3", "level_5", "level_10",
            "objectives_5", "objectives_25", "verified_25", "veteran",
        };

        public static string Achievement(string key)
        {
            switch (key)
            {
                case "founded":       return KmhText.Get("KMH_Fed_AchFounded", "Founded");
                case "first_ally":    return KmhText.Get("KMH_Fed_AchFirstAlly", "First ally");
                case "five_servers":  return KmhText.Get("KMH_Fed_AchFiveServers", "Five servers");
                case "level_3":       return KmhText.Get("KMH_Fed_AchLevel3", "Level 3");
                case "level_5":       return KmhText.Get("KMH_Fed_AchLevel5", "Level 5");
                case "level_10":      return KmhText.Get("KMH_Fed_AchLevel10", "Level 10");
                case "objectives_5":  return KmhText.Get("KMH_Fed_AchObjectives5", "Five objectives");
                case "objectives_25": return KmhText.Get("KMH_Fed_AchObjectives25", "Twenty-five objectives");
                case "verified_25":   return KmhText.Get("KMH_Fed_AchVerified25", "Twenty-five players took part");
                case "veteran":       return KmhText.Get("KMH_Fed_AchVeteran", "Veteran");
                default:              return key ?? "";
            }
        }

        public static string AchievementWhy(string key)
        {
            switch (key)
            {
                case "founded":       return KmhText.Get("KMH_Fed_AchFoundedWhy", "The federation was created.");
                case "first_ally":    return KmhText.Get("KMH_Fed_AchFirstAllyWhy", "Two member servers at the same time.");
                case "five_servers":  return KmhText.Get("KMH_Fed_AchFiveServersWhy", "Five servers have been members.");
                case "level_3":       return KmhText.Get("KMH_Fed_AchLevel3Why", "The federation reached level 3.");
                case "level_5":       return KmhText.Get("KMH_Fed_AchLevel5Why", "The federation reached level 5.");
                case "level_10":      return KmhText.Get("KMH_Fed_AchLevel10Why", "The federation reached level 10.");
                case "objectives_5":  return KmhText.Get("KMH_Fed_AchObjectives5Why", "Five weekly objectives completed.");
                case "objectives_25": return KmhText.Get("KMH_Fed_AchObjectives25Why", "Twenty-five weekly objectives completed.");
                case "verified_25":   return KmhText.Get("KMH_Fed_AchVerified25Why", "Twenty-five different players have taken part, each confirmed by Colony Nexus Global.");
                case "veteran":       return KmhText.Get("KMH_Fed_AchVeteranWhy", "Thirty days old, with at least two member servers.");
                default:              return "";
            }
        }

        public static string Benefit(string key, double value)
        {
            string pct = value.ToString("0.#", CultureInfo.CurrentCulture);
            switch (key)
            {
                case "tag":             return KmhText.Get("KMH_Fed_BenTag", "Federation tag and colour beside your guild's name");
                case "site_output":     return KmhText.Get("KMH_Fed_BenSiteOutput", "Site output +{0}%", pct);
                case "roadworks":       return KmhText.Get("KMH_Fed_BenRoadworks", "Roadworks progress +{0}%", pct);
                case "extra_objective": return KmhText.Get("KMH_Fed_BenExtraObjective", "A fourth weekly objective");
                case "renowned":        return KmhText.Get("KMH_Fed_BenRenowned", "The Renowned badge");
                default:                return key ?? "";
            }
        }

        // Unix seconds in the player's own time zone; a value no clock can hold reads as unknown rather than throwing.
        public static string When(long unixSeconds)
        {
            if (unixSeconds <= 0) return "—";
            try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
            catch (ArgumentOutOfRangeException) { return "—"; }
        }

        public static string TimeLeft(long unixSeconds)
        {
            if (unixSeconds <= 0) return "—";
            try { return DialogLayout.TimeLeft(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcTicks, DateTime.UtcNow.Ticks); }
            catch (ArgumentOutOfRangeException) { return "—"; }
        }

        // The time alone when it was today, so "the copy from 14:05" reads as a time and not a date.
        public static string Clock(long unixSeconds, long nowUnix)
        {
            if (unixSeconds <= 0) return "—";
            try
            {
                DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
                bool today = at.Date == DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, nowUnix)).ToLocalTime().Date;
                return today ? at.ToString("HH:mm", CultureInfo.InvariantCulture) : When(unixSeconds);
            }
            catch (ArgumentOutOfRangeException) { return "—"; }
        }

        public static string SyncLine(Dto.FederationSnapshot s, long nowUnix)
        {
            if (s == null) return "";
            if (!s.InGuild) return KmhText.Get("KMH_Fed_SyncNoGuild", "Join a guild first");
            if (!FederationCache.IsLiveLink(s) && !FederationCache.IsEndedLink(s))
                return KmhText.Get("KMH_Fed_SyncNotLinked", "Your guild is not in a federation");
            if (s.SyncedAt <= 0) return KmhText.Get("KMH_Fed_NotSynced", "Not read from Colony Nexus Global yet");
            string from = Clock(s.SyncedAt, nowUnix);
            if (s.Stale || (!s.Available && s.Unavailable == "global.unreachable"))
            {
                if (s.BenefitsUntil > nowUnix)
                    return KmhText.Get("KMH_Fed_SyncDownBonuses", "Colony Nexus Global unreachable — showing the copy from {0}, bonuses continue until {1}",
                                       from, When(s.BenefitsUntil));
                if (s.BenefitsUntil > 0)
                    return KmhText.Get("KMH_Fed_SyncDownPaused", "Colony Nexus Global unreachable — showing the copy from {0}, bonuses paused until it answers", from);
                return KmhText.Get("KMH_Fed_SyncDown", "Colony Nexus Global unreachable — showing the copy from {0}", from);
            }
            if (!s.Available) return KmhText.Get("KMH_Fed_SyncCopy", "Showing the copy from {0}", from);
            long minutes = Math.Max(0, nowUnix - s.SyncedAt) / 60;
            return minutes < 1 ? KmhText.Get("KMH_Fed_SyncedNow", "Synced just now")
                               : KmhText.Get("KMH_Fed_SyncedAgo", "Synced {0} min ago", minutes);
        }

        public static string Rank(string rank)
        {
            switch (rank ?? "")
            {
                case "owner":     return KmhText.Get("KMH_Fed_RankOwner", "Owner");
                case "admin":     return KmhText.Get("KMH_Fed_RankAdmin", "Admin");
                case "moderator": return KmhText.Get("KMH_Fed_RankModerator", "Moderator");
                case "officer":   return KmhText.Get("KMH_Fed_RankOfficer", "Officer");
                case "member":    return KmhText.Get("KMH_Fed_RankMember", "Member");
                default:          return rank ?? "";
            }
        }
    }
}
