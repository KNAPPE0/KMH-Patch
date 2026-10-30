using System.Collections.Generic;
using KMHPatch.Features.Roadworks.Dto;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Roadworks
{
    internal static class KmhRoutePreview
    {
        private static PlanetTile _from = PlanetTile.Invalid, _to = PlanetTile.Invalid;
        private static readonly List<Vector3> _points = new List<Vector3>();
        private static string _label = "";
        private static bool _ok;
        private static Material _okMat, _badMat;

        // The autopilot cannot move the mouse; a tile id here stands in for the hovered tile.
        internal static int PinnedTileIdForTest = -1;

        internal static string Label => _label;
        internal static int PointCount => _points.Count;

        internal static void Reset()
        {
            _from = PlanetTile.Invalid; _to = PlanetTile.Invalid;
            _points.Clear(); _label = ""; _ok = false;
        }

        private static Material OkMat => _okMat ??= LineMat(UI.KmhBrand.Accent);
        private static Material BadMat => _badMat ??= LineMat(UI.KmhBrand.Bad);

        private static Material LineMat(Color c)
            => MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.WorldOverlayTransparent, c, WorldMaterials.WorldLineRenderQueue);

        // Planned once per hovered tile, not per frame: a world path search is far too heavy to run every frame.
        internal static void Update(PlanetTile from, RoadworksSnapshot snap, string tier)
        {
            PlanetTile to = PinnedTileIdForTest >= 0 ? new PlanetTile(PinnedTileIdForTest, from.Layer) : GenWorld.MouseTile();
            if (to != _to || from != _from) Plan(from, to, snap, tier);
            if (_points.Count < 2) return;
            Material m = _ok ? OkMat : BadMat;
            for (int i = 0; i + 1 < _points.Count; i++) GenDraw.DrawWorldLineBetween(_points[i], _points[i + 1], m, 2.6f);
        }

        // Only the unbuilt stretch is drawn; built segments are real roads already.
        private static Material _plannedMat;
        private static Material PlannedMat => _plannedMat ??= LineMat(UI.KmhBrand.WithAlpha(UI.KmhBrand.Accent, 0.55f));
        private static RoadworksSnapshot _drawnFrom;
        private static readonly List<List<Vector3>> _planned = new List<List<Vector3>>();

        internal static int PlannedSegmentsForTest { get { int n = 0; foreach (var p in _planned) n += p.Count - 1; return n; } }

        internal static void DrawProjects()
        {
            RoadworksSnapshot snap = RoadworksCache.Snapshot;
            if (snap != _drawnFrom) Rebuild(snap);
            foreach (List<Vector3> line in _planned)
                for (int i = 0; i + 1 < line.Count; i++) GenDraw.DrawWorldLineBetween(line[i], line[i + 1], PlannedMat, 2.2f);
        }

        // Rebuilt only when a new snapshot arrives, never per frame.
        private static void Rebuild(RoadworksSnapshot snap)
        {
            _drawnFrom = snap;
            _planned.Clear();
            WorldGrid grid = Find.WorldGrid;
            if (snap?.Projects == null || grid == null) return;
            foreach (RoadProjectDto p in snap.Projects)
            {
                if (p == null || p.State != RoadProjectDto.StateBuilding || p.RouteTiles == null || p.RouteLayers == null) continue;
                if (p.RouteTiles.Count != p.RouteLayers.Count || p.RouteTiles.Count < 2) continue;
                var line = new List<Vector3>();
                for (int i = System.Math.Max(0, p.SegmentsDone); i < p.RouteTiles.Count; i++)
                {
                    Vector3 c = grid.GetTileCenter(new PlanetTile(p.RouteTiles[i], p.RouteLayers[i]));
                    line.Add(c + c.normalized * 0.08f);
                }
                if (line.Count >= 2) _planned.Add(line);
            }
        }

        private static void Plan(PlanetTile from, PlanetTile to, RoadworksSnapshot snap, string tier)
        {
            _from = from; _to = to;
            _points.Clear(); _label = ""; _ok = false;
            if (!to.Valid || snap == null) return;

            if (!KmhRoadRoutePlanner.TryPlan(from, to, snap.MaxRouteSegments, out List<PlanetTile> route, out string why))
            {
                _label = why ?? "";
                return;
            }
            WorldGrid grid = Find.WorldGrid;
            // Lifted off the surface so the line is not buried in the terrain mesh.
            foreach (PlanetTile t in route) { Vector3 c = grid.GetTileCenter(t); _points.Add(c + c.normalized * 0.08f); }

            List<string> keys = KmhRoadQuote.RouteKeys(KmhRoadRoutePlanner.Flatten(route));
            string refusal = KmhRoadQuote.RefusalFor(snap, tier, keys);
            if (refusal != null) { _label = refusal; return; }
            _ok = true;
            _label = $"{keys.Count} segment{(keys.Count == 1 ? "" : "s")} - {KmhRoadQuote.SilverFor(snap, tier, keys)} silver";
        }
    }
}
