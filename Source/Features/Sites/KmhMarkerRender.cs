using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Vanilla lays a world quad texture-up along an east-west tangent, which is invisible on symmetric dots and not on KMH art.
    internal static class KmhMarkerRender
    {
        // Built on first draw, not in a field initialiser: constructing one needs the engine, and the offline suite has none.
        private static MaterialPropertyBlock _block;

        internal static float UprightAngle(Vector3 drawPos)
        {
            Camera cam = Find.WorldCamera;
            return cam == null ? 0f : AngleFor(drawPos, cam.transform.up, cam.transform.forward);
        }

        // Degrees about the normal that put the art's up-axis on screen-up anywhere; camera-free so the suite can drive it.
        internal static float AngleFor(Vector3 drawPos, Vector3 camUp, Vector3 camForward)
        {
            Vector3 n = drawPos.normalized;
            Vector3 east = Vector3.Cross(n, Vector3.up);
            if (east.sqrMagnitude < 1e-6f) return 0f;   // straight over a pole: no east to measure against
            east.Normalize();
            Vector3 side = Vector3.Cross(n, east);   // the quad's other axis; vanilla starts the art along east

            Vector3 want = Vector3.ProjectOnPlane(camUp, n);
            if (want.sqrMagnitude < 1e-6f) want = Vector3.ProjectOnPlane(-camForward, n);
            if (want.sqrMagnitude < 1e-6f) return 0f;

            return Mathf.Atan2(Vector3.Dot(want, side), Vector3.Dot(want, east)) * Mathf.Rad2Deg;
        }

        // Vanilla WorldObject.Draw with that angle supplied; the fade into the screen-space icon stays vanilla's.
        internal static void Draw(WorldObject wo)
        {
            Material mat = wo.Material;
            if (mat == null) return;

            float size  = wo.Tile.Layer.AverageTileSize;
            float angle = UprightAngle(wo.DrawPos);
            float pct   = ExpandableWorldObjectsUtility.RawTransitionPct;
            bool  fade  = wo.def.expandingIcon && pct > 0f && !ExpandableWorldObjectsUtility.HiddenByRules(wo);
            if (fade && pct >= 1f) return;

            Emit(wo.DrawPos, size, wo.DrawAltitude, mat, angle, fade, pct);
        }

        private static void Emit(Vector3 pos, float size, float altitude, Material mat, float angle, bool fade, float pct)
        {
            if (!fade)
            {
                WorldRendererUtility.DrawQuadTangentialToPlanet(pos, size, altitude, mat, angle);
                return;
            }
            Color c = mat.color;
            if (_block == null) _block = new MaterialPropertyBlock();
            _block.SetColor(ShaderPropertyIDs.Color, new Color(c.r, c.g, c.b, c.a * (1f - pct)));
            WorldRendererUtility.DrawQuadTangentialToPlanet(pos, size, altitude, mat, angle, false, false, _block);
        }

        // The art in its own colours; ownership is the outline's job, and tinting this washed a shield into a green blob.
        internal static Material Plain(Texture2D tex)
            => MaterialPool.MatFrom(tex, ShaderDatabase.WorldOverlayTransparentLit, Color.white,
                                    WorldMaterials.WorldObjectRenderQueue);

        // The shader RimWorld draws its own expanding icons with - colorTwo is the border, and the icon comes from the caller. This is the ring an owned settlement wears.
        internal static Material Halo(Color color)
        {
            var req = new MaterialRequest(null, ShaderDatabase.ExpandingIconUI, Color.white)
            { colorTwo = color, needsMainTex = false };
            return MaterialPool.MatFrom(req);
        }
    }
}
