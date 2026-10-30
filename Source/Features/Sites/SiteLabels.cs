using System;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Sites.Dto;

namespace KMHPatch.Features.Sites
{
    // Client-side: only the client resolves linked names, and a system outpost has no OwnerUsername.
    internal static class SiteLabels
    {
        public static string Controller(SiteEntry s)
        {
            if (s == null) return "";
            switch (string.IsNullOrEmpty(s.OwnerKind) ? SiteEntry.OwnerPlayer : s.OwnerKind)
            {
                case SiteEntry.OwnerGuildKind: return string.IsNullOrEmpty(s.ControllingGuild) ? "A guild" : UI.KmhDisplayText.GuildName(s.ControllingGuild);
                case SiteEntry.OwnerNeutral:   return "Unclaimed";
                case SiteEntry.OwnerSystem:    return string.IsNullOrEmpty(s.ControllerFaction) ? "Hostile" : UI.KmhDisplayText.Inert(s.ControllerFaction, 64);
                default:                       return LinkedAccountsCache.Format(s.OwnerUsername);
            }
        }

        // The place's own name, which is not its controller - a derelict ruin is held by nobody, not by itself.
        public static string Name(SiteEntry s)
        {
            if (s == null) return "";
            return !string.IsNullOrEmpty(s.SiteName) ? UI.KmhDisplayText.Inert(s.SiteName, 64) : $"Tile {s.Tile}";
        }

        public static bool IsOutpost(SiteEntry s) => s != null && !string.IsNullOrEmpty(s.OutpostTemplate);

        // Named as the build window names types; an older site with no type reads as Custom.
        public static string Kind(SiteEntry s)
        {
            switch (s?.Archetype)
            {
                case SiteEntry.ArchetypeFarmland:  return "Farmland";
                case SiteEntry.ArchetypeQuarry:    return "Quarry";
                case SiteEntry.ArchetypeWoodland:  return "Woodland";
                case SiteEntry.ArchetypeRanch:     return "Ranch";
                case SiteEntry.ArchetypeRoadworks: return "Roadworks";
                default:                           return "Custom site";
            }
        }

        public static string StateLabel(SiteEntry s)
        {
            switch (s?.OutpostState)
            {
                case SiteEntry.OutpostDerelict:  return "<color=#f0c04f>derelict</color>";
                case SiteEntry.OutpostHostile:   return "<color=#f07167>hostile</color>";
                case SiteEntry.OutpostDefeated:  return "<color=#c4cad6>defeated</color>";
                case SiteEntry.OutpostClaimable: return "<color=#4cc38a>claimable</color>";
                case SiteEntry.OutpostCaptured:  return "<color=#A9AFB9>captured</color>";
                case SiteEntry.OutpostDormant:   return "<color=#A9AFB9>dormant</color>";
                default:                         return "";
            }
        }

        // Presentation only; the server decides.
        public static int ClaimMinutesLeft(SiteEntry s)
        {
            if (s == null || s.ClaimWindowEndsUtcTicks <= 0) return -1;
            long left = s.ClaimWindowEndsUtcTicks - DateTime.UtcNow.Ticks;
            return left <= 0 ? 0 : (int)(left / TimeSpan.TicksPerMinute);
        }

        // A captured outpost should not read like something built from scratch.
        public static string HeritageLine(SiteEntry s)
        {
            if (!IsOutpost(s) || string.IsNullOrEmpty(s.CapturedBy)) return "";
            return $"<color=#A9AFB9>Reclaimed frontier infrastructure · claimed by {UI.KmhDisplayText.GuildName(s.CapturedBy)}</color>";
        }
    }
}
