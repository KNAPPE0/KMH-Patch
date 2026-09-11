using System.Collections.Generic;
using KMHPatch.Features.Roadworks.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Roadworks
{
    // Wire handler for kmh.roadworks.*. Runs on the network thread, so nothing here touches Verse.
    internal static class RoadworksHandler
    {
        public static void Register()
            => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.RoadworksSnapshot, OnSnapshot);

        private static void OnSnapshot(KmhEnvelope env)
        {
            RoadworksSnapshot snap = env?.DataAs<RoadworksSnapshot>();
            if (snap != null) RoadworksCache.Apply(snap);
        }

        // A pre-1.3.0 server has no roadworks handler; with no snapshot arriving the reconciler stays inert.
        public static bool Available => KmhCapabilities.Has(KmhCapabilities.Roadworks);

        // The only way out: contract-check section 19 fails if a second Send appears in this file.
        private static bool SendGated(string kind, object payload, string opId = null)
            => Available && KmhDispatcher.Send(kind, payload, opId);

        public static bool RequestSnapshot() => SendGated(KmhProtocol.Kind.RoadworksRequest, null);

        // Flat parallel arrays: no shape negotiation across client versions.
        public static bool StartProject(int siteTile, string tier, IList<int> routeTiles, IList<int> routeLayers)
            => SendGated(KmhProtocol.Kind.RoadworksStart, new
            {
                site_tile = siteTile, tier,
                route_tiles = routeTiles, route_layers = routeLayers,
            }, SubProtocol.KmhOpId.For($"roadworks.start|{siteTile}|{tier}|{RouteKey(routeTiles, routeLayers)}"));

        // The route is what makes one start distinct from another, so the id has to cover it, not just the site.
        private static string RouteKey(IList<int> tiles, IList<int> layers)
        {
            var sb = new System.Text.StringBuilder();
            if (tiles != null) foreach (int t in tiles) sb.Append(t).Append(',');
            sb.Append('/');
            if (layers != null) foreach (int l in layers) sb.Append(l).Append(',');
            return sb.ToString();
        }

        public static bool CancelProject(long projectId)
            => SendGated(KmhProtocol.Kind.RoadworksCancel, new { project_id = projectId });
    }
}
