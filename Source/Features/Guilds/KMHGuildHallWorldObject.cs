using System.Collections.Generic;
using KMHPatch.UI;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Guilds
{
    // Transient like the site markers: reconciled from the guild snapshot on connect and never persisted.
    public class KMHGuildHallWorldObject : WorldObject
    {
        public string GuildName   = "";
        public int    RadiusTiles;

        public override string Label => string.IsNullOrEmpty(GuildName) ? "Guild Hall" : $"Guild Hall - {GuildName}";

        // White like the sites: the hall's own art stays readable and ownership rides on the halo behind it.
        public override Color ExpandingIconColor => Color.white;

        // The reconciler only ever places your own guild's hall, so its halo is always the "mine" colour.
        internal Color HaloColor => Features.Sites.KmhMarkerColors.Mine;

        public override Material Material
        {
            get
            {
                if (_material != null) return _material;
                Texture2D t = Features.Sites.KmhMarkerArt.NamedIcon("GuildHall");
                return t == null ? base.Material : (_material = Features.Sites.KmhMarkerRender.Plain(t));
            }
        }

        // Read by vanilla when it draws the zoomed-out icon, so the border is RimWorld's own, not a KMH imitation.
        public override Material ExpandingMaterial
        {
            get
            {
                if (!Features.Sites.KmhMarkerColors.Enabled) return null;
                Color c = HaloColor;
                if (_halo == null || _haloColor != c) { _haloColor = c; _halo = Features.Sites.KmhMarkerRender.Halo(c); }
                return _halo;
            }
        }

        private Material _material;
        private Material _halo;
        private Color    _haloColor;

        public override void Draw() => Features.Sites.KmhMarkerRender.Draw(this);

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
                icon         = KMHTextures.Guild ?? BaseContent.BadTex,
                action       = () => Find.WindowStack.Add(new Dialog_KMHGuildHall())
            };
        }
    }
}
