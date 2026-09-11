using KMHPatch.Diagnostics;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Built on Verse.Window, depending on no RWT UI helper, so it stays clean-room.
    public class Dialog_KMHWelcome : Window_KMHBase
    {
        protected override bool ClosesOnSessionEnd => false;

        public enum Step { KMH, OfficialRWT }

        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(560f, 520f);

        private readonly Step   _step;
        private readonly string _title;
        private readonly string _description;

        public Dialog_KMHWelcome() : this(Step.KMH) { }

        public Dialog_KMHWelcome(Step step)
        {
            _step = step;

            if (step == Step.KMH)
            {
                _title       = $"Welcome to {Constants.DisplayName}";
                _description =
                    "A community feature pack for the official RimWorld Together mod. It adds a " +
                    "KMH tab with a marketplace, auctions and a want board, personal and guild " +
                    "treasuries, a quest board, guilds, production sites, roadworks, frontier " +
                    "outposts, chat and player mail, and server-wide events.\n\n" +
                    "It runs as a clean-room patch - no RWT files are modified or bundled - and " +
                    "only activates on a KMH-enabled server.";
            }
            else
            {
                _title       = "Built on the official RimWorld Together";
                _description =
                    "KMH Patch wouldn't exist without the original RimWorld " +
                    "Together mod by Nova and Company. If you enjoy KMH, please " +
                    "show the official team some love by visiting their links " +
                    "below.";
            }

            // Accept/cancel handled manually, or OK would close instead of chaining to step 2.
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
        }

        private Vector2 _scroll;

        private const float FooterH = 46f;
        private const float BtnH    = 38f;
        private const float BtnGap  = 10f;

        protected override void DrawContents(Rect rect)
        {
            float y = 0f;

            // Measured, not fixed: a wrapped title loses its second line and offsets every control below it.
            Text.Font   = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            float titleH = Mathf.Max(40f, Text.CalcHeight(_title, rect.width));
            Widgets.Label(new Rect(0f, y, rect.width, titleH), _title);
            y += titleH + 4f;

            Widgets.DrawLineHorizontal(0f, y, rect.width);
            y += 12f;

            Text.Font   = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            // Links scroll with the description, or wrapped copy pushes them through the OK button and off-screen.
            Rect body = new Rect(0f, y, rect.width, Mathf.Max(DialogLayout.MinBodyHeight, rect.height - y - FooterH));
            DrawBody(body);

            Rect okRect = new Rect((rect.width - 150f) / 2f, rect.height - 42f, 150f, 38f);
            if (Widgets.ButtonText(okRect, "OK"))
            {
                Close();
                if (_step == Step.KMH)
                {
                    Find.WindowStack.Add(new Dialog_KMHWelcome(Step.OfficialRWT));
                }
            }

            // IMGUI globals are shared, so a leaked alignment lands on whatever draws next.
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawBody(Rect area)
        {
            (string label, string url)[] links = _step == Step.KMH
                ? new[]
                  {
                      ("KMH Discord",         Constants.DiscordUrl),
                      ("KMH Patch (GitHub)",  Constants.GitHubUrl),
                      ("KMH Server (GitHub)", Constants.GitHubServerUrl),
                      ("KMH Steam Workshop",  Constants.SteamWorkshopUrl),
                  }
                : new[]
                  {
                      ("Official RWT Discord",    Constants.OfficialRwtDiscordUrl),
                      ("Official RWT GitHub",     Constants.OfficialRwtGitHubUrl),
                      ("Official Steam Workshop", Constants.OfficialRwtSteamWorkshopUrl),
                      ("Support RWT (Ko-fi)",     Constants.OfficialRwtKofiUrl),
                  };

            float viewW = Mathf.Max(1f, area.width - DialogLayout.ScrollbarReserveWidth);
            float descH = Text.CalcHeight(_description, viewW - 16f);
            float contentH = descH + 12f + links.Length * (BtnH + BtnGap);

            Rect view = new Rect(0f, 0f, viewW, Mathf.Max(contentH, area.height));
            Widgets.BeginScrollView(area, ref _scroll, view);

            Widgets.Label(new Rect(8f, 0f, viewW - 16f, descH), _description);

            // Buttons yield width rather than overhanging a narrow window at a fixed size.
            float btnW = Mathf.Min(200f, viewW - 16f);
            float by   = descH + 12f;
            foreach ((string label, string url) in links)
            {
                if (Widgets.ButtonText(new Rect((viewW - btnW) / 2f, by, btnW, BtnH), label)) OpenUrl(url);
                by += BtnH + BtnGap;
            }

            Widgets.EndScrollView();
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Application.OpenURL(url);
            }
            catch (System.Exception ex)
            {
                KmhLog.Warn($"Could not open URL '{url}': {ex.Message}");
            }
        }
    }
}
