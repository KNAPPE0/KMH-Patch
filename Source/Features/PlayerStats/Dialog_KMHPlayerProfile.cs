using UnityEngine;
using Verse;

namespace KMHPatch.Features.PlayerStats
{
    public class Dialog_KMHPlayerProfile : Window_KMHBase
    {
        private readonly PlayerProfilePanel _panel;
        // Opened from Standings on a small screen: docked right and leaves the board clickable.
        private readonly bool _beside;

        public Dialog_KMHPlayerProfile(string username) : this(username, null, false) { }

        internal Dialog_KMHPlayerProfile(string username, string focusColonist, bool beside)
        {
            _panel = new PlayerProfilePanel(username, focusColonist);
            _beside = beside;
            doCloseX = true; forcePause = false; draggable = true; resizeable = true;
            absorbInputAroundWindow = !beside;
        }

        internal string Username => _panel.Username;

        internal void Show(string username, string focusColonist) => _panel.Focus(username, focusColonist);

        public override Vector2 InitialSize => _beside ? KMHPatch.UI.DialogLayout.FitToScreen(520f, Verse.UI.screenHeight - 120f)
                                                       : KMHPatch.UI.DialogLayout.FitToScreen(700f, 640f);

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            if (_beside) windowRect.x = Mathf.Max(0f, Verse.UI.screenWidth - windowRect.width - 12f);
        }

        public override void PostClose()
        {
            base.PostClose();
            _panel.Closed();
        }

        protected override void DrawContents(Rect rect)
        {
            float y = KMHPatch.UI.DialogLayout.DrawTitle(rect, KMHPatch.UI.KmhText.Get("KMH_PP_Title", "Player profile"));
            KMHPatch.UI.DialogLayout.DrawSectionDivider(rect, ref y);
            _panel.Draw(new Rect(0f, y, rect.width, KMHPatch.UI.DialogLayout.BodyHeight(rect, y)), closable: false);
            if (KMHPatch.UI.DialogLayout.DrawCloseButton(rect)) Close();
        }
    }
}
