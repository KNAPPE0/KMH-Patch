using UnityEngine;
using Verse;

namespace KMHPatch.Features.Chat
{
    // Borrows the one ChatVideoPlayer, absorbs no input and pauses nothing, so it can stay open during play.
    public class Dialog_KMHVideoWindow : Window_KMHBase
    {
        private const float MinW = KMHPatchSettings.VideoWindowMinW, MinH = KMHPatchSettings.VideoWindowMinH;
        private bool _placed;

        public Dialog_KMHVideoWindow()
        {
            doCloseX                = true;
            draggable               = true;
            resizeable              = true;
            minSize                 = new Vector2(MinW, MinH);
            forcePause              = false;
            absorbInputAroundWindow = false;
            closeOnAccept           = false;
            closeOnCancel           = false;
            drawShadow              = false;
            onlyOneOfTypeAllowed    = true;
            soundAppear             = null;
            soundClose              = null;
        }

        public override Vector2 InitialSize => SizeWithin(
            KMHPatchMod.Settings?.VideoWindowW ?? KMHPatchSettings.VideoWindowDefW,
            KMHPatchMod.Settings?.VideoWindowH ?? KMHPatchSettings.VideoWindowDefH, MinW, MinH);

        public static void Open()
        {
            if (!ChatVideoPlayer.AnythingPlaying) return;
            if (Find.WindowStack == null || Find.WindowStack.IsOpen(typeof(Dialog_KMHVideoWindow))) return;
            Find.WindowStack.Add(new Dialog_KMHVideoWindow());
        }

        public override void PostOpen()
        {
            base.PostOpen();
            if (_placed) return;
            _placed = true;
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s != null) PlaceAt(s.VideoWindowX, s.VideoWindowY);
        }

        public override void PostClose()
        {
            base.PostClose();
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s == null) return;
            s.VideoWindowX = windowRect.x;
            s.VideoWindowY = windowRect.y;
            s.VideoWindowW = Mathf.Max(MinW, windowRect.width);
            s.VideoWindowH = Mathf.Max(MinH, windowRect.height);
            KMHPatchMod.SaveSettings();

            ChatVideoPlayer.StopIfNobodyWatching();
        }

        protected override void DrawContents(Rect rect)
        {
            if (!ChatVideoPlayer.AnythingPlaying) { Close(); return; }

            const float barH = 26f;
            Rect view = new Rect(0f, 0f, rect.width, Mathf.Max(1f, rect.height - barH - 2f));
            Widgets.DrawBoxSolid(view, new Color(0f, 0f, 0f, 0.94f));

            // Letterboxed to the video's own aspect, so a window the player resized freely never stretches the picture.
            Texture tex = ChatVideoPlayer.Frame;
            if (tex != null)
            {
                float scale = Mathf.Min(view.width / ChatVideoPlayer.Width, view.height / ChatVideoPlayer.Height);
                float w = ChatVideoPlayer.Width * scale, h = ChatVideoPlayer.Height * scale;
                GUI.DrawTexture(new Rect(view.x + (view.width - w) / 2f, view.y + (view.height - h) / 2f, w, h),
                                tex, ScaleMode.ScaleToFit);
            }

            // Only when there is room for it: the transport controls come first on a window this small.
            float defaultW = rect.width >= 300f ? 60f : 0f;
            if (defaultW > 0f)
            {
                Rect defaultR = new Rect(rect.width - defaultW, view.yMax + 2f, defaultW, barH);
                if (Widgets.ButtonText(defaultR, "Default")) ResetToDefault();
                TooltipHandler.TipRegion(defaultR, "Put this window back to its default size and position.");
            }

            // Not fullscreen, so the bar keeps its Fullscreen button and this window simply stays where it is.
            float barW = Mathf.Max(80f, rect.width - defaultW - (defaultW > 0f ? 4f : 0f));
            ChatVideoControls.Draw(new Rect(0f, view.yMax + 2f, barW, barH), inFullscreen: false);
        }

        private void ResetToDefault()
        {
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s != null)
            {
                s.VideoWindowX = s.VideoWindowY = -1f;
                s.VideoWindowW = KMHPatchSettings.VideoWindowDefW;
                s.VideoWindowH = KMHPatchSettings.VideoWindowDefH;
                KMHPatchMod.SaveSettings();
            }
            CentreAt(KMHPatchSettings.VideoWindowDefW, KMHPatchSettings.VideoWindowDefH);
        }
    }
}
