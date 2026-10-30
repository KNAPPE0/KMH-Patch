using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Lets a player judge the animation switches without joining; nothing here actually loads.
    public class Dialog_KMHLoadingPreview : Window_KMHBase
    {
        public override Vector2 InitialSize => DialogLayout.FitToScreen(720f, 560f);

        public Dialog_KMHLoadingPreview()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            draggable = true;
            resizeable = true;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_Load_PreviewTitle", "Loading screen preview"));
            DialogLayout.DrawSectionDivider(rect, ref y);

            string status = KmhText.Get("KMH_Load_PreviewStatus", "Connecting to the server");
            float wide = KmhLoadingPanel.Height(rect.width, true);
            KmhLoadingPanel.Draw(new Rect(0f, y, rect.width, wide), status, -1f, KmhText.Get("KMH_Load_PreviewDetail", "Preview: nothing is loading"));
            y += wide + 12f;

            float w = Mathf.Min(380f, rect.width);
            float narrow = KmhLoadingPanel.Height(w, false);
            if (y + narrow < rect.height - 52f)
                KmhLoadingPanel.Draw(new Rect((rect.width - w) / 2f, y, w, narrow), status);

            Rect full = new Rect(0f, rect.height - 34f, Mathf.Min(240f, rect.width / 2f - 6f), 34f);
            if (Widgets.ButtonText(full, KmhText.Get("KMH_Load_PreviewFull", "Preview full screen")))
                Find.WindowStack.Add(new Dialog_KMHLoadingScenePreview());
            if (DialogLayout.DrawCloseButton(rect)) Close();
        }
    }

    public class Dialog_KMHLoadingScenePreview : Window
    {
        private readonly float _opened = Time.realtimeSinceStartup;

        public override Vector2 InitialSize => new Vector2(Verse.UI.screenWidth, Verse.UI.screenHeight);
        protected override float Margin => 0f;

        public Dialog_KMHLoadingScenePreview()
        {
            doWindowBackground = false;
            drawShadow = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            closeOnCancel = true;
            forcePause = false;
            layer = WindowLayer.Super;
        }

        protected override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(0f, 0f, Verse.UI.screenWidth, Verse.UI.screenHeight);
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Six seconds of a real share, then four of "not measurable", so both bars can be judged.
            float t = (Time.realtimeSinceStartup - _opened) % 10f;
            float progress = t < 6f ? t / 6f : -1f;
            string status = t < 6f ? "LoadingAssets".Translate().ToString() : KmhText.Get("KMH_Load_PreviewStatus", "Connecting to the server");
            KmhLoadingScreen.DrawScene(inRect, status, progress, KmhText.Get("KMH_Load_PreviewDetail", "Preview: nothing is loading"));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = KmhBrand.WithAlpha(KmhBrand.Muted, 0.7f);
            Widgets.Label(new Rect(0f, 14f, inRect.width, 20f), KmhText.Get("KMH_Load_PreviewExit", "Click or press Esc to close the preview."));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (Event.current.type == EventType.MouseDown) { Close(); Event.current.Use(); }
        }
    }
}
