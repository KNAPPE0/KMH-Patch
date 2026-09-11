using System.Collections.Generic;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Sites;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using Verse;

namespace KMHPatch.Features.Wealth.Sources
{
    // Output at my own sites is one button press from being mine, so it counts or a warehouse is a raid shelter; other players' sites are excluded even when visible.
    internal sealed class SiteStorageWealthSource : IKmhWealthSource
    {
        public string Name => "Site storage";

        public float SilverValue()
        {
            List<SiteEntry> sites = SiteCache.Snapshot?.Sites;
            string me = KmhSession.Me;
            if (sites == null || string.IsNullOrEmpty(me)) return 0f;

            float v = 0f;
            foreach (SiteEntry s in sites)
            {
                if (s?.StoredItems == null) continue;
                bool mineOutright = KmhSession.Same(s.OwnerUsername, me);
                // A guild site's storage is one press away for any member, so it counts - as a share like the vault, or every member is credited the whole pile.
                bool guildHeld = !mineOutright && KmhGuildShare.ControlledByMyGuild(s.ControllingGuild);
                if (!mineOutright && !guildHeld) continue;

                float here = 0f;
                foreach (KeyValuePair<string, int> kv in s.StoredItems)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail((kv.Key ?? "").Split('|')[0]);
                    if (def != null && kv.Value > 0) here += def.BaseMarketValue * kv.Value;
                }
                v += guildHeld ? KmhGuildShare.Of(here, GuildCache.Guild, me) : here;
            }
            return v;
        }
    }
}
