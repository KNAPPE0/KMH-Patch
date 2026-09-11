using System;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.Wealth.Sources
{
    // The member's share of the CURRENT vault, or donating would erase wealth from threat scaling; weighted as a ratio because lifetime contributions never decrement.
    internal sealed class GuildVaultWealthSource : IKmhWealthSource
    {
        public string Name => "Guild vault";

        public float SilverValue()
        {
            GuildSnapshot g = GuildCache.Guild;
            string me = KmhSession.Me;
            if (g?.Members == null || g.Members.Count == 0 || string.IsNullOrEmpty(me)) return 0f;

            // Guild, not Snapshot: Snapshot is whichever vault was fetched last, so the share would come and go with whatever window was opened.
            float vault = KmhWealthValue.OfTreasury(Treasury.TreasuryCache.Guild);
            if (vault <= 0f) return 0f;

            return KmhGuildShare.Of(vault, g, me);
        }
    }
}
