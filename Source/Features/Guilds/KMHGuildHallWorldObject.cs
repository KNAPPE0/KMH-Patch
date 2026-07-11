using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Guilds
{
    // World-map marker for the player's guild hall. Transient like KMH site markers: reconciled from the guild
    // snapshot by WorldComponent_KMHSiteMarkers, never persisted (rebuilt on connect).
    public class KMHGuildHallWorldObject : WorldObject
    {
        public string GuildName   = "";
        public int    RadiusTiles;

        public override string Label => string.IsNullOrEmpty(GuildName) ? "Guild Hall" : $"Guild Hall - {GuildName}";

        public override string GetInspectString()
        {
            string s = "KMH Guild Hall";
            if (!string.IsNullOrEmpty(GuildName)) s += $"\nGuild: {GuildName}";
            if (RadiusTiles > 0) s += $"\nAccess radius: {RadiusTiles} tile(s)";
            return s;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            yield return new Command_Action
            {
                defaultLabel = "Open Guild Hall",
                defaultDesc  = "Open the KMH Guild Hall dialog (members, vault, perks, diplomacy).",
                icon         = BaseContent.BadTex,
                action       = () => Find.WindowStack.Add(new Dialog_KMHGuildHall())
            };
        }
    }
}
