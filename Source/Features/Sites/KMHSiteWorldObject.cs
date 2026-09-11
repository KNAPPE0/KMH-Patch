using System.Collections.Generic;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Transient marker: sites are server state, so a patch keeps these out of the save and the reconciler rebuilds them.
    public class KMHSiteWorldObject : WorldObject
    {
        public string SiteOwner   = "";
        public string SiteItem    = "";
        public int    SiteWorkers;
        public int    SiteMaxWorkers;

        // The entry this marker was last reconciled from. Drives the label and the map look; never saved.
        public SiteEntry Entry;

        public override string Label => KmhMarkerArt.LabelFor(Entry, SiteItem);

        // Overridden because the def names one texture, so a site changed identity as the player zoomed.
        public override Material Material
        {
            get
            {
                Texture2D t = KmhMarkerArt.BaseIconFor(Entry);
                // Falling back means the close-zoom marker wears the def's Generic art; say so once, not per frame.
                if (t == null) { NoteFallbackOnce(); return base.Material; }
                string key = KmhMarkerArt.ArtKeyFor(Entry);
                if (_material == null || _materialKey != key)
                {
                    _materialKey = key;
                    _material    = KmhMarkerRender.Plain(t);
                }
                return _material;
            }
        }

        // Read by vanilla when it draws the zoomed-out icon, so the border is RimWorld's own, not a KMH imitation.
        public override Material ExpandingMaterial
        {
            get
            {
                if (!KmhMarkerArt.TryHaloFor(Entry, MineToManage, out Color c)) return null;
                if (_halo == null || _haloColor != c) { _haloColor = c; _halo = KmhMarkerRender.Halo(c); }
                return _halo;
            }
        }

        private Material _material;
        private string   _materialKey;
        private Material _halo;
        private Color    _haloColor;
        private bool     _notedFallback;

        public override void Draw() => KmhMarkerRender.Draw(this);

        private void NoteFallbackOnce()
        {
            if (_notedFallback) return;
            _notedFallback = true;
            try
            {
                KMHPatch.Diagnostics.KmhLog.Warn(
                    $"Marker tile {Tile.tileId}: close-zoom art '{KmhMarkerArt.NormalPathFor(Entry)}' did not load - "
                  + "falling back to the def's texture.");
            }
            catch { }
        }

        // Fallback is for a missing asset only; it lands on the small-dot art, but a marker must still draw.
        public override Texture2D ExpandingIcon => KmhMarkerArt.IconFor(Entry) ?? base.ExpandingIcon;

        // Resolved by the reconciler rather than here: this is read every frame the world draws.
        public bool MineToManage;

        // White: the zoomed-out icon keeps its own colours too, and the halo patch draws ownership behind it.
        public override Color ExpandingIconColor => Color.white;

        public override float ExpandingIconPriority => KmhMarkerArt.PriorityFor(Entry, base.ExpandingIconPriority);

        public override string GetInspectString()
        {
            string s = KmhMarkerArt.IsOutpost(Entry) ? "KMH Frontier outpost" : "KMH production site";

            string archetype = KmhMarkerArt.ArchetypeWord(Entry?.Archetype);
            if (archetype.Length > 0) s += $"\nType: {archetype}";

            if (KmhMarkerArt.IsOutpost(Entry))
            {
                string state = KmhMarkerArt.StateWord(Entry.OutpostState);
                if (state.Length > 0) s += $"\nState: {state}";
                if (Entry.CanClaim) s += "\n<color=#FFD65C>You can claim this location.</color>";
                if (!string.IsNullOrEmpty(Entry.CapturedBy)) s += $"\nTaken by: {Entry.CapturedBy}";
            }

            if (!string.IsNullOrEmpty(SiteItem))  s += $"\nProduces: {SiteItem}";
            if (!string.IsNullOrEmpty(SiteOwner)) s += $"\nOwner: {SiteOwner}";
            if (Entry != null && Entry.Stability < 100) s += $"\nCondition: {Entry.Stability}%";
            s += $"\nWorkers: {SiteWorkers}/{SiteMaxWorkers}";
            return s;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            yield return new Command_Action
            {
                defaultLabel = "Manage KMH sites",
                defaultDesc  = "Open the KMH sites dialog to join as a worker, set the reward destination, or cancel.",
                icon         = KMHTextures.Sites ?? BaseContent.BadTex,
                action       = () => Find.WindowStack.Add(new Dialog_KMHSites())
            };
        }
    }
}
