using KMHPatch.Features.Guilds;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.Sites
{
    // Display only; the server decides. Reads the kind rather than testing OwnerUsername for empty.
    internal static class SiteOwnershipClient
    {
        public static string KindOf(SiteEntry s)
            => s == null || string.IsNullOrEmpty(s.OwnerKind) ? SiteEntry.OwnerPlayer : s.OwnerKind;

        public static bool IsPlayerControlled(SiteEntry s)
        {
            string k = KindOf(s);
            return k == SiteEntry.OwnerPlayer || k == SiteEntry.OwnerGuildKind;
        }

        // Mirrors SiteOwnership.CanManage: a guild site has NO OwnerUsername, so testing that field hides every control.
        public static bool CanManage(SiteEntry s, string username)
        {
            if (s == null || string.IsNullOrEmpty(username)) return false;
            switch (KindOf(s))
            {
                case SiteEntry.OwnerPlayer:
                    return !string.IsNullOrEmpty(s.OwnerUsername) && KmhSession.Same(s.OwnerUsername, username);
                case SiteEntry.OwnerGuildKind:
                    return !string.IsNullOrEmpty(s.ControllingGuild)
                           && string.Equals(GuildCache.Guild?.Name ?? "", s.ControllingGuild,
                                            System.StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }
    }
}
