using UnityEngine;
using Verse;

namespace KMHPatch.Features.Chat
{
    // One transport bar drawn both inline and fullscreen, so neither teaches different controls. Degrades by width.
    internal static class ChatVideoControls
    {
        public const float BarHeight = 24f;

        private const float Gap      = 4f;
        private const float BtnSmall = 48f;
        private const float BtnWide  = 62f;
        private const float TimeW    = 88f;
        private const float VolW     = 68f;
        private const float MinSeekW = 48f;

        private static string _drag;

        // Returns true when the player asked to cross into or out of fullscreen; the caller owns that window.
        public static bool Draw(Rect bar, bool inFullscreen)
        {
            bool toggleFullscreen = false;
            float h = bar.height;

            // Right cluster first: these are fixed-width, and the seek bar takes what is left over.
            float rx = bar.xMax;

            rx -= BtnWide;
            if (Widgets.ButtonText(new Rect(rx, bar.y, BtnWide, h), inFullscreen ? "Exit" : "Full"))
                toggleFullscreen = true;
            TooltipHandler.TipRegion(new Rect(rx, bar.y, BtnWide, h),
                inFullscreen ? "Leave fullscreen (Esc or F). The video keeps playing in chat."
                             : "Watch fullscreen. Esc or F comes back here without interrupting playback.");
            rx -= Gap;

            // Offered only from the chat row: the other two views already ARE somewhere to watch it.
            if (!inFullscreen && bar.width >= 440f && !Find.WindowStack.IsOpen(typeof(Dialog_KMHVideoWindow)))
            {
                rx -= BtnWide;
                Rect popR = new Rect(rx, bar.y, BtnWide, h);
                if (Widgets.ButtonText(popR, "Pop out")) Dialog_KMHVideoWindow.Open();
                TooltipHandler.TipRegion(popR,
                    "Move the video into a small window you can drag anywhere and keep open while you play.");
                rx -= Gap;
            }

            if (bar.width >= 380f)
            {
                rx -= BtnSmall;
                Rect loopR = new Rect(rx, bar.y, BtnSmall, h);
                if (Widgets.ButtonText(loopR, ChatVideoPlayer.Looping ? "Loop" : "Once")) ChatVideoPlayer.ToggleLoop();
                TooltipHandler.TipRegion(loopR, ChatVideoPlayer.Looping ? "Repeating. Click to play through once." : "Plays once. Click to repeat.");
                rx -= Gap;
            }

            if (bar.width >= 300f)
            {
                rx -= VolW;
                Rect volR = new Rect(rx, bar.y + 6f, VolW, h - 12f);
                float shown = ChatVideoPlayer.Muted ? 0f : ChatVideoPlayer.Volume;
                if (Bar(volR, "vol", shown, out float picked)) ChatVideoPlayer.SetVolume(picked);
                TooltipHandler.TipRegion(new Rect(rx, bar.y, VolW, h),
                    "Volume. This is scaled by RimWorld's master volume, so the game's own slider still applies.");
                rx -= Gap;
            }

            rx -= BtnSmall;
            Rect muteR = new Rect(rx, bar.y, BtnSmall, h);
            // Never colour alone: the label itself says which state it is in.
            if (Widgets.ButtonText(muteR, ChatVideoPlayer.Muted ? "Unmute" : "Mute")) ChatVideoPlayer.ToggleMute();
            rx -= Gap;

            float x = bar.x;
            Rect playR = new Rect(x, bar.y, BtnSmall, h);
            bool restart = ChatVideoPlayer.Finished;
            if (Widgets.ButtonText(playR, ChatVideoPlayer.Paused || restart ? "Play" : "Pause")) ChatVideoPlayer.TogglePause();
            TooltipHandler.TipRegion(playR, restart ? "Watch it again from the start (Space in fullscreen)."
                                             : "Pause or resume (Space in fullscreen).");
            x += BtnSmall + Gap;

            if (Widgets.ButtonText(new Rect(x, bar.y, BtnSmall, h), "Stop")) ChatVideoPlayer.Stop();
            x += BtnSmall + Gap;

            // Fit, not a width threshold: a fixed one drew the clock straight through the mute button.
            double len = ChatVideoPlayer.Length;
            if (len > 0d && rx - x >= TimeW + Gap + MinSeekW)
            {
                Widgets.Label(new Rect(x, bar.y + 2f, TimeW, h),
                    $"<color=grey>{ChatVideoPlayer.FormatTime(ChatVideoPlayer.Position)} / {ChatVideoPlayer.FormatTime(len)}</color>");
                x += TimeW + Gap;
            }

            // A live stream has no length to scrub, so it gets a state word instead of a bar that would do nothing.
            float seekW = rx - x;
            if (seekW >= MinSeekW)
            {
                Rect seekR = new Rect(x, bar.y + 6f, seekW, h - 12f);
                if (ChatVideoPlayer.CanSeek)
                {
                    float fill = len > 0d ? (float)(ChatVideoPlayer.Position / len) : 0f;
                    if (Bar(seekR, "seek", fill, out float at)) ChatVideoPlayer.Seek(at * len);
                    TooltipHandler.TipRegion(new Rect(x, bar.y, seekW, h), "Click or drag to seek. Arrow keys jump 5 seconds in fullscreen.");
                }
                else
                {
                    Widgets.Label(new Rect(x, bar.y + 2f, seekW, h), "<color=grey>live</color>");
                }
            }

            return toggleFullscreen;
        }

        // Buffering, paused and finished all look identical on screen: a still picture that is not moving.
        public static string StatusWord()
        {
            if (ChatVideoPlayer.Buffering) return "buffering…";
            if (ChatVideoPlayer.Paused)    return "paused";
            if (ChatVideoPlayer.Finished)  return "finished - press play to watch it again";
            return null;
        }

        // Fullscreen keyboard shortcuts. Returns true when the player asked to leave.
        public static bool HandleKeys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return false;

            switch (e.keyCode)
            {
                case KeyCode.Space:      ChatVideoPlayer.TogglePause(); e.Use(); return false;
                case KeyCode.M:          ChatVideoPlayer.ToggleMute();  e.Use(); return false;
                case KeyCode.L:          ChatVideoPlayer.ToggleLoop();  e.Use(); return false;
                case KeyCode.LeftArrow:  ChatVideoPlayer.Nudge(-5d);    e.Use(); return false;
                case KeyCode.RightArrow: ChatVideoPlayer.Nudge(5d);     e.Use(); return false;
                case KeyCode.F:          e.Use(); return true;
                default: return false;
            }
        }

        // Hand-rolled rather than Widgets.HorizontalSlider so the press registers on the bar's own rect in a scroll view.
        private static bool Bar(Rect r, string id, float fill, out float picked)
        {
            picked = 0f;

            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.12f));
            float f = Mathf.Clamp01(fill);
            if (f > 0f) Widgets.DrawBoxSolid(new Rect(r.x, r.y, r.width * f, r.height), UI.KmhTheme.Accent);

            Event e = Event.current;
            if (e == null) return false;

            if (e.type == EventType.MouseDown && e.button == 0 && Mouse.IsOver(r)) _drag = id;

            bool mine = _drag == id;
            bool report = mine && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp);

            // rawType, not type: a release outside the bar still ends the drag, or it follows the mouse forever.
            if (mine && e.rawType == EventType.MouseUp) _drag = null;

            if (!report) return false;
            picked = Mathf.Clamp01((e.mousePosition.x - r.x) / Mathf.Max(1f, r.width));
            e.Use();
            return true;
        }
    }
}
