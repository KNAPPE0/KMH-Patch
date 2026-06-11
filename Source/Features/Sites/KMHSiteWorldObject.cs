using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Sites
{
    // World-map marker for a KMH custom site. KMH sites are server state, so these markers are transient -
    // WorldComponent_KMHSiteMarkers creates, updates, and removes them from the site snapshot. Detail fields are
    // set by the reconciler, not persisted, since they're rebuilt on connect
    public class KMHSiteWorldObject : WorldObject
    {
        public string SiteOwner   = "";
        public string SiteItem    = "";
        public int    SiteWorkers;
        public int    SiteMaxWorkers;

        public override string Label => string.IsNullOrEmpty(SiteItem) ? "KMH site" : $"KMH site - {SiteItem}";

        public override string GetInspectString()
        {
            string s = "KMH production site";
            if (!string.IsNullOrEmpty(SiteItem))  s += $"\nProduces: {SiteItem}";
            if (!string.IsNullOrEmpty(SiteOwner)) s += $"\nOwner: {SiteOwner}";
            s += $"\nWorkers: {SiteWorkers}/{SiteMaxWorkers}";
            return s;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            yield return new Command_Action
            {
                defaultLabel = "Manage KMH sites",
                defaultDesc  = "Open the KMH sites dialog to join as a worker, set the reward destination, or cancel.",
                icon         = BaseContent.BadTex,
                action       = () => Find.WindowStack.Add(new Dialog_KMHSites())
            };
        }
    }
}
