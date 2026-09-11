using System;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Sites.Dto;

namespace KMHPatch.Features.Sites
{
    // Built client-side because only the client resolves linked names, and a system outpost has no OwnerUsername at all.
    internal static class SiteLabels
    {
        public static string Controller(SiteEntry s)
        {
            if (s == null) return "";
            switch (string.IsNullOrEmpty(s.OwnerKind) ? SiteEntry.OwnerPlayer : s.OwnerKind)
            {
                case SiteEntry.OwnerGuildKind: return string.IsNullOrEmpty(s.ControllingGuild) ? "A guild" : s.ControllingGuild;
                case SiteEntry.OwnerNeutral:   return "Unclaimed";
                case SiteEntry.OwnerSystem:    return string.IsNullOrEmpty(s.ControllerFaction) ? "Hostile" : s.ControllerFaction;
                default:                       return LinkedAccountsCache.Format(s.OwnerUsername);
            }
        }

        // The place's own name, which is not its controller - a derelict ruin is held by nobody, not by itself.
        public static string Name(SiteEntry s)
        {
            if (s == null) return "";
            return !string.IsNullOrEmpty(s.SiteName) ? s.SiteName : $"Tile {s.Tile}";
        }

        public static bool IsOutpost(SiteEntry s) => s != null && !string.IsNullOrEmpty(s.OutpostTemplate);

        public static string StateLabel(SiteEntry s)
        {
            switch (s?.OutpostState)
            {
                case SiteEntry.OutpostDerelict:  return "<color=#ffcf59>derelict</color>";
                case SiteEntry.OutpostHostile:   return "<color=#ff7676>hostile</color>";
                case SiteEntry.OutpostDefeated:  return "<color=#cccccc>defeated</color>";
                case SiteEntry.OutpostClaimable: return "<color=#80ff80>claimable</color>";
                case SiteEntry.OutpostCaptured:  return "<color=grey>captured</color>";
                case SiteEntry.OutpostDormant:   return "<color=grey>dormant</color>";
                default:                         return "";
            }
        }

        // Minutes left in a claim window, or -1 when there is no window. Presentation only; the server decides.
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
            return $"<color=grey>Reclaimed frontier infrastructure · claimed by {s.CapturedBy}</color>";
        }
    }
}
