using System;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Mods;
using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Reads LongEventHandler's private state, so a renamed member turns this screen off, never the load.
    internal static class KmhLoadingScreen
    {
        private static bool _resolved, _broken, _kmhSession;
        private static FieldInfo _current, _levelLoadOp, _eventText;
        private static PropertyInfo _standard;
        private static float _shownAt = -10f, _lastDrawn = -10f;
        private static int _tip;

        // As LongEventsOnGUI decides it; a small window over the map (an autosave, a new map) stays vanilla.
        internal static bool FullScreenNow()
        {
            object ev = CurrentEvent();
            if (ev == null) return false;
            bool standard = _standard != null && _standard.GetValue(ev, null) is bool b && b;
            return !standard || Find.UIRoot == null || Find.WindowStack == null;
        }

        // False when this game version has renamed what the screen reads, or a draw threw this session.
        internal static bool Available { get { Resolve(); return !_broken; } }

        internal static bool Wanted()
        {
            if (_broken) return false;
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s != null && !s.ColonyNexusLoadingScreen) return false;
            // Held from join start until RWT disconnects, so a world loading before the handshake still gets the screen.
            if (KmhDispatcher.IsKmhServer || KmhJoinPreparation.Connecting) _kmhSession = true;
            else if (_kmhSession && RwtCompat.NetworkState == "Disconnected") _kmhSession = false;
            return _kmhSession;
        }

        // A real share only where RimWorld measures one; otherwise no progress is claimed.
        internal static void Draw()
        {
            object ev = CurrentEvent();
            string status = ev != null ? _eventText?.GetValue(ev) as string : null;
            float progress = -1f;
            // The one load RimWorld measures: the scene's own assets, as vanilla's "Loading assets 45%".
            if (_levelLoadOp?.GetValue(null) is AsyncOperation op && !op.isDone)
            {
                progress = op.progress;
                status = "LoadingAssets".Translate();
            }
            if (string.IsNullOrEmpty(status)) status = "LoadingLongEvent".Translate();
            string server = KmhDispatcher.IsKmhServer ? KmhServerLabel.Current() : KmhJoinPreparation.ServerName;
            DrawScene(new Rect(0f, 0f, Verse.UI.screenWidth, Verse.UI.screenHeight), status, progress, server);
        }

        internal static void DrawScene(Rect screen, string status, float progress, string server)
        {
            var m = KmhLoadingPanel.Motion.Now();
            float now = Time.realtimeSinceStartup;
            if (now - _lastDrawn > 1f) { _shownAt = now; _tip = Mathf.Abs((int)(now * 7f)) % TipCount; }
            _lastDrawn = now;
            // The sky is there at once; what sits on it fades in, so the cut from the menu is not a jump.
            float fade = m.Functional ? Mathf.Clamp01((now - _shownAt) / 0.45f) : 1f;

            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            try
            {
                Sky(screen, m, fade);

                float h = screen.height, w = screen.width;
                float s = Mathf.Clamp(h * 0.19f, 96f, 210f);
                float wordH = Mathf.Max(30f, s * 0.30f);
                bool hasServer = !string.IsNullOrEmpty(server);
                float block = s + 18f + wordH + (hasServer ? 30f : 8f) + 26f + 4f + 14f + 22f;
                float top = Mathf.Max(24f, h * 0.47f - block / 2f);

                Rect mark = new Rect(screen.center.x - s / 2f, top, s, s);
                float warmth = progress >= 0f ? 0.85f + 0.35f * Mathf.Clamp01(progress) : 1f;
                KmhBrand.DrawGlow(mark.ExpandedBy(s * 0.85f), KmhBrand.Accent, m.Breath(0.20f, 0.05f, 1.1f) * fade * warmth);
                KmhBrand.DrawMark(mark, fade, m.Moon());

                float y = mark.yMax + 18f;
                float wordW = Mathf.Min(w - 48f, wordH * 6f);
                Rect word = new Rect(screen.center.x - wordW / 2f, y, wordW, wordH);
                KmhBrand.DrawWordmark(word, KmhBrand.WithAlpha(KmhBrand.Ink, fade));
                KmhBrand.DrawWordmarkSweep(word, m.Sweep(7f, 1.8f), fade);
                y += wordH + 8f;

                float barW = Mathf.Clamp(w * 0.26f, 240f, 480f);
                if (hasServer)
                {
                    if (server != _serverRaw) { _serverRaw = server; _serverShown = KmhLoadingPanel.Clean(server).ToUpperInvariant(); }
                    Tracked(new Rect(screen.center.x - barW / 2f - 40f, y, barW + 80f, 20f),
                            _serverShown, KmhBrand.WithAlpha(KmhBrand.Accent, fade));
                    y += 22f;
                }
                y += 26f;

                KmhLoadingPanel.DrawBar(new Rect(screen.center.x - barW / 2f, y, barW, 4f), progress, m);
                y += 4f + 14f;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperCenter;
                GUI.color = KmhBrand.WithAlpha(KmhBrand.Ink2, fade);
                string line = KmhLoadingPanel.Clean(status) + (progress >= 0f ? "  ·  " + Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f) + "%" : "");
                Widgets.Label(new Rect(screen.center.x - 300f, y, 600f, 24f), line);

                Tip(screen, m, now, fade);
                Footer(screen, fade);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }

        private static void Sky(Rect screen, KmhLoadingPanel.Motion m, float fade)
        {
            Widgets.DrawBoxSolid(screen, KmhBrand.Bg);
            float h = screen.height;
            KmhBrand.DrawGlow(new Rect(screen.center.x - h * 0.75f, h * 0.47f - h * 0.75f, h * 1.5f, h * 1.5f), KmhBrand.Accent, 0.10f);
            KmhBrand.DrawGlow(new Rect(-h * 0.55f, -h * 0.65f, h * 1.3f, h * 1.3f), KmhBrand.Cool, 0.08f);

            for (int i = 0; i < StarCount; i++)
            {
                float hx = Hash(i * 3 + 1), hy = Hash(i * 3 + 2), hs = Hash(i * 3 + 3);
                float size = 1f + hs * 1.6f;
                float twinkle = m.Decorative ? 0.65f + 0.35f * Mathf.Sin(m.T * (0.5f + hs) + i * 1.7f) : 0.85f;
                float a = (0.12f + 0.45f * hs) * twinkle * fade;
                KmhBrand.DrawDisc(new Rect(screen.x + hx * screen.width - size / 2f, screen.y + hy * screen.height - size / 2f, size, size),
                                  KmhBrand.WithAlpha(KmhBrand.Ink, a));
            }
        }

        private const int StarCount = 110;

        // Stable per star, so the sky does not reshuffle between frames or loads.
        private static float Hash(int i)
        {
            uint x = (uint)i * 2654435761u;
            x ^= x >> 13; x *= 1274126177u; x ^= x >> 16;
            return (x & 0xFFFFFF) / (float)0x1000000;
        }

        // Measured once per text, not every frame: a load is when the game thread can least spare it.
        private static string _serverRaw, _serverShown, _trackedText;
        private static string[] _trackedGlyphs = new string[0];
        private static float[] _trackedWidths = new float[0];
        private static float _trackedTotal;

        // IMGUI has no letter spacing: placed glyph by glyph, then centred as a whole.
        private static void Tracked(Rect r, string text, Color color)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            const float gap = 1.6f;
            if (text != _trackedText)
            {
                // A lone space measures as nothing, which ran "THIS PC ONLY" together; its width is what it adds between two letters.
                float space = Mathf.Max(3f, Text.CalcSize("a a").x - Text.CalcSize("aa").x);
                _trackedText = text;
                _trackedGlyphs = new string[text.Length];
                _trackedWidths = new float[text.Length];
                _trackedTotal = 0f;
                for (int i = 0; i < text.Length; i++)
                {
                    _trackedGlyphs[i] = text[i].ToString();
                    _trackedWidths[i] = text[i] == ' ' ? space : Text.CalcSize(_trackedGlyphs[i]).x;
                    _trackedTotal += _trackedWidths[i] + gap;
                }
            }
            float total = _trackedTotal;
            float[] widths = _trackedWidths;
            if (total > r.width)
            {
                string cut = text.Truncate(r.width * 0.92f);
                Text.Anchor = TextAnchor.UpperCenter;
                GUI.color = color;
                Widgets.Label(r, cut);
                return;
            }
            float x = r.center.x - total / 2f;
            GUI.color = color;
            for (int i = 0; i < text.Length; i++)
            {
                Widgets.Label(new Rect(x, r.y, widths[i] + 4f, r.height), _trackedGlyphs[i]);
                x += widths[i] + gap;
            }
        }

        private static void Tip(Rect screen, KmhLoadingPanel.Motion m, float now, float fade)
        {
            int index = m.Functional ? (_tip + (int)((now - _shownAt) / 9f)) % TipCount : _tip;
            string tip = TipText(index);
            float w = Mathf.Min(screen.width - 64f, 760f);
            Text.Font = GameFont.Small;
            if (index != _tipMeasured || w != _tipWidth) { _tipMeasured = index; _tipWidth = w; _tipHeight = Text.CalcHeight(tip, w); }
            float th = _tipHeight;
            float y = screen.yMax - th - 46f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = KmhBrand.WithAlpha(KmhBrand.Accent, 0.9f * fade);
            Widgets.Label(new Rect(screen.center.x - w / 2f, y - 20f, w, 18f), KmhText.Get("KMH_Tip_Label", "Colony Nexus tip"));
            Text.Font = GameFont.Small;
            GUI.color = KmhBrand.WithAlpha(KmhBrand.Muted, fade);
            Widgets.Label(new Rect(screen.center.x - w / 2f, y, w, th + 4f), tip);
        }

        private const int TipCount = 8;
        private static int _tipMeasured = -1;
        private static float _tipWidth, _tipHeight;

        private static string TipText(int i)
        {
            switch (i)
            {
                case 0: return KmhText.Get("KMH_Tip_1", "Open the Colony Nexus tab to trade with other players: the Marketplace, the Want Board and timed auctions.");
                case 1: return KmhText.Get("KMH_Tip_2", "Your treasury vault keeps silver and goods off the map. A deposit counts once your colony has been saved.");
                case 2: return KmhText.Get("KMH_Tip_3", "Guilds share a vault, ranks and perks. Start one or accept an invite in the Guild Hall.");
                case 3: return KmhText.Get("KMH_Tip_4", "Frontier Operations are derelict and contested places anyone can claim, for themselves or for their guild.");
                case 4: return KmhText.Get("KMH_Tip_5", "Roadworks plans a route on the world map and quotes the cost before you commit to building it.");
                case 5: return KmhText.Get("KMH_Tip_6", "Server Standings ranks players, guilds and colonies. The website shows every board for this server.");
                case 6: return KmhText.Get("KMH_Tip_7", "Mail can carry silver and items, and you can recall it while it is still unread.");
                default: return KmhText.Get("KMH_Tip_8", "Link Steam under Account so your name and history follow you to every Colony Nexus server.");
            }
        }

        private static readonly string VersionLine = BuildVersionLine();

        private static string BuildVersionLine()
        {
            Version v = typeof(KmhLoadingScreen).Assembly.GetName().Version;
            return $"{KmhBrand.Name} {v.ToString(v.Revision > 0 ? 4 : 3)} · {KmhProtocol.UiBuildTag}";
        }

        private static void Footer(Rect screen, float fade)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerRight;
            GUI.color = KmhBrand.WithAlpha(KmhBrand.Muted, 0.55f * fade);
            Widgets.Label(new Rect(screen.xMax - 420f, screen.yMax - 26f, 408f, 20f), VersionLine);
        }

        internal static void Broken(Exception ex)
        {
            _broken = true;
            KmhLog.Warn($"Colony Nexus loading screen turned off for this session, RimWorld's own window is back: {ex.Message}");
        }

        private static object CurrentEvent()
        {
            Resolve();
            return _current?.GetValue(null);
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                _current = AccessTools.Field(typeof(LongEventHandler), "currentEvent");
                _levelLoadOp = AccessTools.Field(typeof(LongEventHandler), "levelLoadOp");
                Type ev = _current?.FieldType;
                _eventText = ev == null ? null : AccessTools.Field(ev, "eventText");
                _standard = ev == null ? null : AccessTools.Property(ev, "UseStandardWindow");
                if (_current == null || _eventText == null || _standard == null)
                    Broken(new MissingMemberException("LongEventHandler", _current == null ? "currentEvent" : _eventText == null ? "eventText" : "UseStandardWindow"));
            }
            catch (Exception ex) { Broken(ex); }
        }
    }
}
