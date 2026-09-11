using UnityEngine;
using Verse;

namespace KMHPatch.Features.Chat
{
    // Borrows the one ChatVideoPlayer, so entering and leaving fullscreen never re-buffers or re-contacts the host.
    public class Dialog_KMHVideo : Window_KMHBase
    {
        // How long the controls stay up after the last mouse movement.
        private const float IdleHideSeconds = 2.5f;

        private Vector2 _lastMouse;
        private float   _movedAt;

        public Dialog_KMHVideo()
        {
            doCloseX                = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside   = false;
            closeOnAccept           = false;
            closeOnCancel           = true;    // Esc leaves fullscreen
            forcePause              = false;   // multiplayer: the world does not stop because someone watched a clip
            draggable               = false;
            resizeable              = false;
            doWindowBackground      = false;   // painted below, edge to edge
            drawShadow              = false;
            onlyOneOfTypeAllowed    = true;
            soundAppear             = null;
            soundClose              = null;
            _movedAt                = Time.realtimeSinceStartup;
        }

        public override Vector2 InitialSize => new Vector2(Verse.UI.screenWidth, Verse.UI.screenHeight);
        protected override float Margin => 0f;

        protected override void SetInitialSizeAndPosition()
            => windowRect = new Rect(0f, 0f, Verse.UI.screenWidth, Verse.UI.screenHeight);

        public static void Open()
        {
            if (!ChatVideoPlayer.AnythingPlaying) return;
            if (Find.WindowStack == null || Find.WindowStack.IsOpen(typeof(Dialog_KMHVideo))) return;
            Find.WindowStack.Add(new Dialog_KMHVideo());
        }

        // With the hub already closed behind it, leaving fullscreen would strand a stream with nowhere to draw.
        public override void PostClose()
        {
            base.PostClose();
            try
            {
                if (Find.WindowStack != null && !Find.WindowStack.IsOpen(typeof(Comms.Dialog_KMHComms)))
                    ChatVideoPlayer.Stop();
            }
            catch { }
        }

        protected override void DrawContents(Rect inRect)
        {
            // Stopped, failed, or swapped for another clip while fullscreen: there is nothing to be fullscreen about.
            if (!ChatVideoPlayer.AnythingPlaying) { Close(false); return; }

            Widgets.DrawBoxSolid(inRect, new Color(0f, 0f, 0f, 0.94f));

            if (ChatVideoControls.HandleKeys()) { Close(false); return; }

            // Clicking counts as activity too, or a player who clicks without moving is clicking at hidden controls.
            Vector2 mouse = Event.current != null ? Event.current.mousePosition : _lastMouse;
            if ((mouse - _lastMouse).sqrMagnitude > 4f
                || (Event.current != null && Event.current.type == EventType.MouseDown))
            {
                _lastMouse = mouse;
                _movedAt = Time.realtimeSinceStartup;
            }

            Rect bar   = new Rect(inRect.x + 16f, inRect.yMax - ChatVideoControls.BarHeight - 12f,
                                  inRect.width - 32f, ChatVideoControls.BarHeight);
            bool showControls = Time.realtimeSinceStartup - _movedAt < IdleHideSeconds
                                || ChatVideoPlayer.Paused || Mouse.IsOver(bar);

            // The bar floats over the picture rather than shrinking it, so hiding the controls never resizes the video.
            Rect frame = ChatVideoPlayer.FitInto(ChatVideoPlayer.Width, ChatVideoPlayer.Height, inRect.ContractedBy(8f));
            Texture tex = ChatVideoPlayer.Frame;
            if (tex != null) GUI.DrawTexture(frame, tex, ScaleMode.ScaleToFit);

            string status = ChatVideoControls.StatusWord();
            if (status != null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(inRect.x, frame.center.y - 14f, inRect.width, 28f),
                              $"<color=white>{status}</color>");
                Text.Anchor = TextAnchor.UpperLeft;
            }

            // Taken before the bar is drawn so the bar's own buttons still win over this click-to-pause.
            if (!Mouse.IsOver(bar) && Widgets.ButtonInvisible(frame)) ChatVideoPlayer.TogglePause();

            if (!showControls) return;

            Widgets.DrawBoxSolid(new Rect(bar.x - 8f, bar.y - 8f, bar.width + 16f, bar.height + 16f),
                                 new Color(0f, 0f, 0f, 0.55f));
            if (ChatVideoControls.Draw(bar, inFullscreen: true)) { Close(false); return; }

            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(inRect.x, inRect.y + 10f, inRect.width - 16f, 24f),
                          "<color=grey>Esc or F to exit · Space pause · M mute · ←/→ seek 5s</color>");
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
