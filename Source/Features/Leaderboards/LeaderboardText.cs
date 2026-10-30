using KMHPatch.Features.Leaderboards.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.Leaderboards
{
    // Keyed by never-changing board keys; a board this client does not know keeps the server's wording.
    internal static class LeaderboardText
    {
        internal static string Title(LeaderboardBoard b)
        {
            if (b == null) return "";
            switch (b.Key)
            {
                case "economy":       return KmhText.Get("KMH_LbT_Economy", "Economy score");
                case "donated":       return KmhText.Get("KMH_LbT_Donated", "Silver donated");
                case "vault":         return KmhText.Get("KMH_LbT_Vault", "Vault value");
                case "trade":         return KmhText.Get("KMH_LbT_Trade", "Trade earnings");
                case "largest-sale":  return KmhText.Get("KMH_LbT_LargestSale", "Largest sale");
                case "purchases":     return KmhText.Get("KMH_LbT_Purchases", "Purchases");
                case "contracts":     return KmhText.Get("KMH_LbT_Contracts", "Contracts completed");
                case "streak":        return KmhText.Get("KMH_LbT_Streak", "Contract streak");
                case "posted":        return KmhText.Get("KMH_LbT_Posted", "Contracts posted");
                case "sites-built":   return KmhText.Get("KMH_LbT_SitesBuilt", "Sites built");
                case "sites-held":    return KmhText.Get("KMH_LbT_SitesHeld", "Sites held");
                case "site-output":   return KmhText.Get("KMH_LbT_SiteOutput", "Site production");
                case "frontier":      return KmhText.Get("KMH_LbT_Frontier", "Frontier captures");
                case "worker-xp":     return KmhText.Get("KMH_LbT_WorkerXp", "Worker XP");
                case "active":        return KmhText.Get("KMH_LbT_Active", "Active time");
                case "reputation":    return KmhText.Get("KMH_LbT_Reputation", "Reputation");
                case "colony-wealth": return KmhText.Get("KMH_LbT_ColonyWealth", "Colony wealth");
                case "kills":         return KmhText.Get("KMH_LbT_Kills", "Kills");
                case "raids":         return KmhText.Get("KMH_LbT_Raids", "Raids survived");
                case "colony-age":    return KmhText.Get("KMH_LbT_ColonyAge", "Colony age");
                case "guild-members": return KmhText.Get("KMH_LbT_GuildMembers", "Guild members");
                case "guild-treasury":return KmhText.Get("KMH_LbT_GuildTreasury", "Guild treasury");
                default:              return KmhDisplayText.Inert(b.Title ?? "", 80);
            }
        }

        internal static string Description(LeaderboardBoard b)
        {
            if (b == null) return "";
            switch (b.Key)
            {
                case "economy":       return KmhText.Get("KMH_LbD_Economy", "Silver donated, half of trade earnings, 100 per contract, 50 per site built and a tenth of worker XP.");
                case "donated":       return KmhText.Get("KMH_LbD_Donated", "Silver given to guilds, net of withdrawals.");
                case "vault":         return KmhText.Get("KMH_LbD_Vault", "What the player holds in Colony Nexus storage on this server, valued by the server.");
                case "trade":         return KmhText.Get("KMH_LbD_Trade", "Silver earned from marketplace and auction sales.");
                case "largest-sale":  return KmhText.Get("KMH_LbD_LargestSale", "The single most valuable sale.");
                case "purchases":     return KmhText.Get("KMH_LbD_Purchases", "Silver spent buying from other players.");
                case "contracts":     return KmhText.Get("KMH_LbD_Contracts", "Bounties, deliveries, hunts and defences completed.");
                case "streak":        return KmhText.Get("KMH_LbD_Streak", "Contracts completed in a row since the last failure.");
                case "posted":        return KmhText.Get("KMH_LbD_Posted", "Contracts offered to other players.");
                case "sites-built":   return KmhText.Get("KMH_LbD_SitesBuilt", "New sites established.");
                case "sites-held":    return KmhText.Get("KMH_LbD_SitesHeld", "Sites the player owns right now.");
                case "site-output":   return KmhText.Get("KMH_LbD_SiteOutput", "Silver produced by the player's sites.");
                case "frontier":      return KmhText.Get("KMH_LbD_Frontier", "Sites captured on the frontier.");
                case "worker-xp":     return KmhText.Get("KMH_LbD_WorkerXp", "Experience the player's colonists earned working sites.");
                case "active":        return KmhText.Get("KMH_LbD_Active", "Time spent actively playing on this server.");
                case "reputation":    return KmhText.Get("KMH_LbD_Reputation", "Contracts honoured, less contracts rejected or abandoned.");
                case "colony-wealth": return KmhText.Get("KMH_LbD_ColonyWealth", "The colony's wealth as the player's game reports it.");
                case "kills":         return KmhText.Get("KMH_LbD_Kills", "Kills by the colony's colonists, hunted animals included, as its game reports.");
                case "raids":         return KmhText.Get("KMH_LbD_Raids", "Raids the colony has survived, as its game reports.");
                case "colony-age":    return KmhText.Get("KMH_LbD_ColonyAge", "Days since the colony was founded, as its game reports.");
                case "guild-members": return KmhText.Get("KMH_LbD_GuildMembers", "Members in each guild.");
                case "guild-treasury":return KmhText.Get("KMH_LbD_GuildTreasury", "Silver in each guild's vault.");
                default:              return KmhDisplayText.Inert(b.Description ?? "", 400);
            }
        }

        internal static string Group(string group)
        {
            switch (group)
            {
                case "Economy":   return KmhText.Get("KMH_LbG_Economy", "Economy");
                case "Trade":     return KmhText.Get("KMH_LbG_Trade", "Trade");
                case "Contracts": return KmhText.Get("KMH_LbG_Contracts", "Contracts");
                case "Sites":     return KmhText.Get("KMH_LbG_Sites", "Sites");
                case "Activity":  return KmhText.Get("KMH_LbG_Activity", "Activity");
                case "Colony":    return KmhText.Get("KMH_LbG_Colony", "Colony");
                case "Guilds":    return KmhText.Get("KMH_LbG_Guilds", "Guilds");
                default:          return KmhDisplayText.Inert(group ?? "", 40);
            }
        }
    }
}
