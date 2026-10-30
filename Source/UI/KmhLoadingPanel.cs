using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Plain IMGUI with textures made here, so it draws the same on 1.6 and 1.7.
    public static class KmhLoadingPanel
    {
        public static float Height(float width, bool detail) => width < 420f ? (detail ? 204f : 186f) : (detail ? 132f : 120f);

        // Progress below zero means "not measurable": the bar travels instead of claiming a share it does not know.
        public static void Draw(Rect r, string status, float progress = -1f, string detail = null)
        {
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) return;
            Motion m = Motion.Now();
            Color old = GUI.color;
            TextAnchor anchor = Text.Anchor;
            Widgets.DrawBoxSolid(r, KmhBrand.Surface);
            GUI.color = KmhBrand.LineStrong;
            Widgets.DrawBox(r, 1);
            GUI.color = old;
            GUI.BeginGroup(r);
            // A group left open by a throw corrupts every window drawn after it.
            try { Contents(new Rect(0f, 0f, r.width, r.height), status, progress, detail, m); }
            finally
            {
                GUI.EndGroup();
                GUI.color = old;
                Text.Anchor = anchor;
                Text.Font = GameFont.Small;
            }
        }

        private static void Contents(Rect local, string status, float progress, string detail, Motion m)
        {
            bool compact = local.width < 420f;
            bool hasDetail = !string.IsNullOrEmpty(detail);
            float s = compact ? 64f : Mathf.Clamp(local.height - 36f, 56f, 92f);
            Rect mark = compact ? new Rect((local.width - s) / 2f, 14f, s, s) : new Rect(20f, (local.height - s) / 2f, s, s);
            KmhBrand.DrawGlow(mark.ExpandedBy(s * 0.45f), KmhBrand.Accent, m.Breath(0.16f, 0.04f, 1.3f));
            KmhBrand.DrawMark(mark, 1f, m.Moon());

            float x = compact ? 16f : mark.xMax + 22f, w = local.width - x - (compact ? 16f : 20f);
            float y = compact ? mark.yMax + 10f : (local.height - (hasDetail ? 92f : 76f)) / 2f;
            TextAnchor align = compact ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;

            KmhBrand.DrawWordmark(new Rect(x, y, w, 24f), KmhBrand.Ink, align);
            y += 30f;
            Text.Font = GameFont.Small;
            GUI.color = KmhBrand.Ink2;
            // One line each, cut with the whole text on hover: a longer translation wrapped and lost its second line.
            DialogLayout.LabelTrunc(new Rect(x, y, w, 22f), Clean(status), align);
            y += 22f;
            if (hasDetail)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = KmhBrand.Muted;
                DialogLayout.LabelTrunc(new Rect(x, y, w, 18f), Clean(detail), align);
                Text.Font = GameFont.Small;
                y += 18f;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            DrawBar(new Rect(x, y + 10f, w, 4f), progress, m);
        }

        // A status is the caller's text; markup in it is shown, never obeyed.
        internal static string Clean(string s) => KmhDisplayText.Inert(s ?? "", 140);

        internal static void DrawBar(Rect r, float progress, Motion m)
        {
            Widgets.DrawBoxSolid(r, KmhBrand.Line);
            Rect fill;
            if (progress >= 0f) fill = new Rect(r.x, r.y, r.width * Mathf.Clamp01(progress), r.height);
            else
            {
                float w = r.width * 0.28f;
                float p = m.Functional ? Mathf.SmoothStep(0f, 1f, Mathf.PingPong(m.T * 0.55f, 1f)) : 0.36f;
                fill = new Rect(r.x + (r.width - w) * p, r.y, w, r.height);
            }
            if (fill.width <= 0.5f) return;
            Widgets.DrawBoxSolid(fill, KmhBrand.Accent);
            // A brighter tip, so the bar reads as moving forward rather than as a block of colour.
            Rect head = new Rect(Mathf.Max(fill.x, fill.xMax - 6f), fill.y, Mathf.Min(6f, fill.width), fill.height);
            Widgets.DrawBoxSolid(head, KmhBrand.AccentStrong);
            if (m.Functional)
                KmhBrand.DrawGlow(new Rect(fill.xMax - 14f, r.center.y - 14f, 28f, 28f), KmhBrand.Accent, 0.35f);
        }

        // What may move: Animated off stops everything; Reduce motion keeps only what reports progress.
        internal struct Motion
        {
            public float T;
            public bool Functional, Decorative;

            public static Motion Now()
            {
                var s = KMHPatchMod.Settings;
                return For(s == null || s.AnimatedVisuals, s != null && s.ReduceMotion, Time.realtimeSinceStartup);
            }

            internal static Motion For(bool animate, bool calm, float time)
                => new Motion { T = animate ? time : 0f, Functional = animate, Decorative = animate && !calm };

            public float Moon() => Decorative ? T * (Mathf.PI * 2f / 9f) : float.NaN;

            public float Breath(float level, float depth, float speed) => Decorative ? level + depth * Mathf.Sin(T * speed) : level;

            // Where a light sweep is, 0..1, for its first `duration` seconds of every `period`; -1 the rest of the time.
            public float Sweep(float period, float duration)
            {
                if (!Decorative) return -1f;
                float s = (T % period) / duration;
                return s < 1f ? Mathf.SmoothStep(0f, 1f, s) : -1f;
            }
        }
    }
}
