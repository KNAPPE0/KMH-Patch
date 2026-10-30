using System;
using System.IO;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Mirrors the website's palette and mark (PublicSite.Mark); change both together or the product stops reading as one.
    internal static class KmhBrand
    {
        public const string Name = "Colony Nexus";

        // The website's dark theme (PublicSite.CssDark).
        public static readonly Color Bg = Hex(0x0b0e13), BgSoft = Hex(0x10141b), Surface = Hex(0x141922), Surface2 = Hex(0x1a202b);
        public static readonly Color Line = Hex(0x242c39), LineStrong = Hex(0x333d4e);
        public static readonly Color Ink = Hex(0xeceff4), Ink2 = Hex(0xc4cad6), Muted = Hex(0x929bab);
        public static readonly Color Accent = Hex(0xf0a24a), AccentStrong = Hex(0xffbb6b);
        public static readonly Color Ok = Hex(0x4cc38a), Warn = Hex(0xf0c04f), Bad = Hex(0xf07167), Cool = Hex(0x5a82ff), Info = Hex(0x82acff);
        public static readonly Color Ring = Hex(0xb9bfca);

        public static Color Hex(int rgb, float a = 1f)
            => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // The mark in the website's 32-unit box, y running down: a world, a ring tilted -22 degrees, a moon.
        private static readonly Vector2 WorldC = new Vector2(16f, 17f), MoonAt = new Vector2(27f, 6.5f);
        private const float WorldR = 8f, RingRx = 14f, RingRy = 4.6f, RingTilt = -22f, MoonR = 2f;

        private sealed class MarkSet { public Texture2D Back, World, Front; }
        private static MarkSet _small, _large;
        private static Texture2D _disc, _glow, _wordmark;
        private static bool _wordmarkTried;

        // moon is radians along its orbit; NaN puts it where the website does.
        public static void DrawMark(Rect r, float alpha = 1f, float moon = float.NaN)
        {
            if (Event.current.type != EventType.Repaint) return;
            float s = Mathf.Min(r.width, r.height);
            if (s < 4f) return;
            Rect box = new Rect(r.center.x - s / 2f, r.center.y - s / 2f, s, s);
            // Real pixels decide the set: a 512 drawn at 30 pixels shimmers wherever mipmaps are missing.
            MarkSet set = s * Prefs.UIScale > 150f ? Large() : Small();
            if (set == null) return;

            Orbit(float.IsNaN(moon) ? 0f : moon, out Vector2 at, out float radius, out bool front);
            Rect moonRect = new Rect(box.x + (at.x - radius) * s / 32f, box.y + (at.y - radius) * s / 32f, radius * 2f * s / 32f, radius * 2f * s / 32f);
            // Behind the world the moon is a little dimmer: depth without a second shape.
            Color moonTint = WithAlpha(front ? Muted : Color.Lerp(Muted, Bg, 0.25f), alpha);

            Color old = GUI.color;
            try
            {
                GUI.color = WithAlpha(Ring, alpha);
                GUI.DrawTexture(box, set.Back);
                if (!front) { GUI.color = moonTint; GUI.DrawTexture(moonRect, Disc()); }
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(box, set.World);
                GUI.color = WithAlpha(Ring, alpha);
                GUI.DrawTexture(box, set.Front);
                if (front) { GUI.color = moonTint; GUI.DrawTexture(moonRect, Disc()); }
            }
            finally { GUI.color = old; }
        }

        // The moon's orbit: through the website's moon position at 0, behind the world for the half above its line.
        internal static void Orbit(float a, out Vector2 at, out float radius, out bool front)
        {
            Vector2 u = MoonAt - WorldC;
            float major = u.magnitude;
            u /= major;
            Vector2 n = new Vector2(-u.y, u.x);
            float sin = Mathf.Sin(a);
            at = WorldC + u * (major * Mathf.Cos(a)) + n * (5f * sin);
            radius = MoonR * (1f + 0.12f * sin);
            front = sin > 0f;
        }

        public static void DrawGlow(Rect r, Color tint, float alpha)
        {
            if (Event.current.type != EventType.Repaint || alpha <= 0.001f) return;
            Color old = GUI.color;
            GUI.color = WithAlpha(tint, alpha);
            GUI.DrawTexture(r, Glow());
            GUI.color = old;
        }

        public static void DrawDisc(Rect r, Color tint)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, Disc());
            GUI.color = old;
        }

        public static bool Button(Rect r, string label, bool primary, string tip = null)
        {
            bool over = Mouse.IsOver(r);
            Color fill = primary ? (over ? AccentStrong : Accent) : (over ? Surface2 : WithAlpha(Surface2, 0.6f));
            Widgets.DrawBoxSolid(r, fill);
            Color old = GUI.color;
            if (!primary)
            {
                GUI.color = over ? WithAlpha(Accent, 0.8f) : LineStrong;
                Widgets.DrawBox(r, 1);
            }
            GameFont f = Text.Font; TextAnchor a = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = primary ? Hex(0x1c1205) : Ink;
            Widgets.Label(r, label.Truncate(r.width - 8f));
            Text.Font = f; Text.Anchor = a;
            GUI.color = old;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        public static bool Tab(Rect r, string label, bool selected)
        {
            bool over = Mouse.IsOver(r);
            Widgets.DrawBoxSolid(r, selected ? WithAlpha(Accent, 0.16f) : over ? Surface2 : WithAlpha(Surface2, 0.6f));
            Color old = GUI.color;
            GUI.color = selected ? WithAlpha(Accent, 0.75f) : over ? WithAlpha(Accent, 0.5f) : LineStrong;
            Widgets.DrawBox(r, 1);
            if (selected) Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 2f, r.width, 2f), Accent);
            GameFont f = Text.Font; TextAnchor a = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = selected ? AccentStrong : over ? Ink : Ink2;
            Widgets.Label(r, (label ?? "").Truncate(r.width - 8f));
            Text.Font = f; Text.Anchor = a;
            GUI.color = old;
            return Widgets.ButtonInvisible(r);
        }

        // Null when the file cannot be read; the caller then writes the name instead.
        public static Texture2D Wordmark
        {
            get
            {
                if (_wordmarkTried) return _wordmark;
                _wordmarkTried = true;
                _wordmark = LoadUncompressed(Path.Combine("Textures", "KMHPatch", "Brand", "Wordmark.png"))
                         ?? ContentFinder<Texture2D>.Get("KMHPatch/Brand/Wordmark", reportFailure: false);
                return _wordmark;
            }
        }

        public static void DrawWordmark(Rect r, Color tint, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            Texture2D tex = Wordmark;
            Color old = GUI.color;
            if (tex != null)
            {
                float h = Mathf.Min(r.height, r.width * tex.height / tex.width), w = h * tex.width / tex.height;
                float x = anchor == TextAnchor.MiddleLeft ? r.x : r.center.x - w / 2f;
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.color = tint;
                    GUI.DrawTexture(new Rect(x, r.center.y - h / 2f, w, h), tex);
                }
            }
            else
            {
                GameFont f = Text.Font; TextAnchor a = Text.Anchor;
                Text.Font = GameFont.Medium;
                Text.Anchor = anchor;
                GUI.color = tint;
                Widgets.Label(r, Name);
                Text.Font = f; Text.Anchor = a;
            }
            GUI.color = old;
        }

        // Fallback redraws the picture through bell-weighted strips, so only the letters under the band brighten.
        public static void DrawWordmarkSweep(Rect r, float t, float alpha, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            Texture2D tex = Wordmark;
            if (tex == null || t <= 0f || t >= 1f || alpha <= 0f || Event.current.type != EventType.Repaint) return;
            float h = Mathf.Min(r.height, r.width * tex.height / tex.width), w = h * tex.width / tex.height;
            Rect word = new Rect(anchor == TextAnchor.MiddleLeft ? r.x : r.center.x - w / 2f, r.center.y - h / 2f, w, h);
            if (KmhShaders.DrawMaskedSweep(word, tex, t, alpha * 0.8f, AccentStrong)) return;
            float band = Mathf.Max(20f, w * 0.14f), centre = word.x - band + (word.width + band * 2f) * t;
            const int Strips = 12;
            Color old = GUI.color;
            for (int i = 0; i < Strips; i++)
            {
                float u = (i + 0.5f) / Strips * 2f - 1f;
                Rect strip = new Rect(centre + u * band - band / Strips, word.y, band * 2f / Strips + 0.5f, word.height);
                GUI.BeginGroup(strip);
                GUI.color = WithAlpha(AccentStrong, alpha * 0.8f * Mathf.Exp(-u * u * 3f));
                GUI.DrawTexture(new Rect(word.x - strip.x, word.y - strip.y, word.width, word.height), tex);
                GUI.EndGroup();
            }
            GUI.color = old;
        }

        // Not via RimWorld's loader: it may compress it or, on 1.7, not have uploaded it when a loading screen needs it.
        private static Texture2D LoadUncompressed(string relative)
        {
            try
            {
                string root = ModRoot();
                if (root == null) return null;
                string path = Path.Combine(root, relative);
                if (!File.Exists(path)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "KMH." + Path.GetFileNameWithoutExtension(path) };
                if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
                tex.filterMode = FilterMode.Trilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply(true, true);
                return tex;
            }
            catch (Exception) { return null; }
        }

        private static string ModRoot()
        {
            var own = typeof(KMHPatchMod).Assembly;
            foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                foreach (var a in m.assemblies.loadedAssemblies)
                    if (a == own) return m.RootDir;
            return null;
        }

        private static MarkSet Small() => _small ?? (_small = Build(128, 1.7f));
        private static MarkSet Large() => _large ?? (_large = Build(512, 1.3f));

        // Coverage from edge distance keeps curves smooth at any size; the whole ring lies beneath, so no seam shows.
        private static MarkSet Build(int n, float stroke)
        {
            float u = n / 32f;
            var back = new Color32[n * n];
            var world = new Color32[n * n];
            var front = new Color32[n * n];
            float rad = -RingTilt * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Color hi = Hex(0xffd7a3), mid = Accent, lo = Hex(0xb4560f);
            Vector2 focus = new Vector2(WorldC.x - 0.28f * WorldR, WorldC.y - 0.40f * WorldR);
            float margin = 1.5f / u;

            for (int y = 0; y < n; y++)
            {
                // Texture rows start at the bottom; the box's y runs down.
                float py = 32f - (y + 0.5f) / u;
                for (int x = 0; x < n; x++)
                {
                    float px = (x + 0.5f) / u;
                    float dx = px - WorldC.x, dy = py - WorldC.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    int i = y * n + x;

                    float qx = dx * cos - dy * sin, qy = dx * sin + dy * cos;
                    float f = qx * qx / (RingRx * RingRx) + qy * qy / (RingRy * RingRy) - 1f;
                    float gx = 2f * qx / (RingRx * RingRx), gy = 2f * qy / (RingRy * RingRy);
                    float d = f / Mathf.Max(1e-4f, Mathf.Sqrt(gx * gx + gy * gy));
                    float ring = Mathf.Clamp01((stroke / 2f - Mathf.Abs(d)) * u + 0.5f);
                    if (ring > 0f)
                    {
                        back[i] = new Color32(255, 255, 255, (byte)(ring * 255f));
                        float over = qy > 0f ? Mathf.Clamp01((WorldR + margin - dist) / margin) : 0f;
                        if (over > 0f) front[i] = new Color32(255, 255, 255, (byte)(ring * over * 255f));
                    }

                    float cover = Mathf.Clamp01((WorldR - dist) * u + 0.5f);
                    if (cover > 0f)
                    {
                        // The website's radial light: centred up and to the left, reaching 80% of the world's box.
                        float t = Mathf.Clamp01(Mathf.Sqrt((px - focus.x) * (px - focus.x) + (py - focus.y) * (py - focus.y)) / (1.6f * WorldR));
                        Color c = t < 0.42f ? Color.Lerp(hi, mid, t / 0.42f) : Color.Lerp(mid, lo, (t - 0.42f) / 0.58f);
                        world[i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(cover * 255f));
                    }
                }
            }
            return new MarkSet { Back = Upload(n, back, "Back"), World = Upload(n, world, "World"), Front = Upload(n, front, "Front") };
        }

        private static Texture2D Upload(int n, Color32[] px, string part)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                name = $"KMH.Mark{part}{n}", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D Disc() => _disc ?? (_disc = Radial(64, d => Mathf.Clamp01((0.5f - d) * 64f)));
        private static Texture2D Glow() => _glow ?? (_glow = Radial(128, d => { float k = Mathf.Clamp01(d * 2f); return (1f - k) * (1f - k); }));

        private static Texture2D Radial(int n, Func<float, float> alpha)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(alpha(Mathf.Sqrt(dx * dx + dy * dy)) * 255f));
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
