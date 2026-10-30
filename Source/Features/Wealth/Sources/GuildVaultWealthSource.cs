using System;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.Wealth.Sources
{
    // A share of the CURRENT vault, or donating erases wealth; a ratio, because lifetime contributions never decrement.
    internal sealed class GuildVaultWealthSource : IKmhWealthSource
    {
        public string Name => "Guild vault";

        public float SilverValue() => ShareOf(GuildCache.Guild, KmhSession.Me);

        internal static float ShareOf(GuildSnapshot g, string me)
        {
            if (g?.Members == null || g.Members.Count == 0 || string.IsNullOrEmpty(me)) return 0f;

            // From the guild snapshot every member receives: the treasury kind only ever carries the personal vault,
            // so a guild vault read from TreasuryCache was always empty and hid its wealth from raid scaling.
            float vault = KmhWealthValue.OfGuildVault(g);
            if (vault <= 0f) return 0f;

            return KmhGuildShare.Of(vault, g, me);
        }
    }
}
