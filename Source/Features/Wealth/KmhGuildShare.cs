using System;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Guilds.Dto;

namespace KMHPatch.Features.Wealth
{
    // Guild-held value splits once across members (by contribution, else evenly): crediting each member the full amount would multiply server wealth by guild size.
    internal static class KmhGuildShare
    {
        // 0 when the caller is not in this guild, so nothing credits wealth to someone who cannot reach it.
        public static float Of(float guildHeldValue, GuildSnapshot g, string me)
        {
            if (guildHeldValue <= 0f || g?.Members == null || g.Members.Count == 0 || string.IsNullOrEmpty(me)) return 0f;

            long mine = 0, total = 0;
            bool amMember = false;
            foreach (GuildMemberDto m in g.Members)
            {
                if (m == null) continue;
                long c = Math.Max(0, m.SilverContributed);
                total += c;
                if (string.Equals(m.Username, me, StringComparison.OrdinalIgnoreCase)) { mine = c; amMember = true; }
            }
            if (!amMember) return 0f;

            return total > 0 ? guildHeldValue * ((float)mine / total) : guildHeldValue / g.Members.Count;
        }

        public static bool ControlledByMyGuild(string controllingGuild)
            => !string.IsNullOrEmpty(controllingGuild)
               && string.Equals(GuildCache.Guild?.Name ?? "", controllingGuild, StringComparison.OrdinalIgnoreCase);
    }
}
