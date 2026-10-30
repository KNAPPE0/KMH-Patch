using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    public class Dialog_KMHAbout : Window_KMHBase
    {
        protected override bool ClosesOnSessionEnd => false;

        // Sized so every button clears the close button, not to the paragraph alone.
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(560f, 620f);

        public Dialog_KMHAbout()
        {
            doCloseButton = true;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
        }

        private Vector2 _scroll;
        private float _contentH = 560f, _measuredH = 560f;
        private int _contentFrame = -1;

        protected override void DrawContents(Rect inRect)
        {
            // Scrolls above the close button, or at 1280x720 with UI scale the last buttons fall off the window.
            Rect content = new Rect(inRect.x, inRect.y, inRect.width, Mathf.Max(KMHPatch.UI.DialogLayout.MinBodyHeight, inRect.height - 45f));
            if (KMHPatch.UI.KmhScroll.NewFrame(ref _contentFrame)) _contentH = _measuredH;
            Rect view = new Rect(0f, 0f, content.width - KMHPatch.UI.DialogLayout.ScrollbarReserveWidth, Mathf.Max(_contentH, content.height));
            Widgets.BeginScrollView(content, ref _scroll, view);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(view);

            Text.Font = GameFont.Medium;
            listing.Label($"{Constants.DisplayName}");
            Text.Font = GameFont.Small;

            listing.Gap(6f);
            listing.Label(
                "Colony Nexus is a community feature pack for the official RimWorld Together mod: " +
                "a marketplace, auctions and a want board, personal and guild treasuries, " +
                "quests, guilds, production sites, roadworks, frontier outposts, chat and " +
                "player mail, and server-wide events. It runs as a separate patch - no " +
                "RimWorld Together files are modified."
            );

            listing.GapLine(12f);

            // The build tag tells builds apart; the assembly version is the same for every build.
            listing.Label($"Version: {SubProtocol.KmhProtocol.DisplayVersion} · {SubProtocol.KmhProtocol.UiBuildTag}");
            listing.Label($"Package: {Constants.PackageId}");

            listing.Gap(12f);

            ButtonThenGap(listing, "Patch GitHub (client mod)", () => Application.OpenURL(Constants.GitHubUrl));
            ButtonThenGap(listing, "Server Addon GitHub", () => Application.OpenURL(Constants.GitHubServerUrl));
            ButtonThenGap(listing, "Open Discord server", () => Application.OpenURL(Constants.DiscordUrl));
            ButtonThenGap(listing, "Show welcome dialog again", () => Find.WindowStack.Add(new Dialog_KMHWelcome()));
            ButtonThenGap(listing, "What's new in this version", () => Find.WindowStack.Add(new Dialog_KMHWhatsNew()));
            ButtonThenGap(listing, "Open Colony Nexus log folder", () => Application.OpenURL(KmhLog.LogFolderPath));

            if (listing.ButtonText("View Colony Nexus log in-game"))
            {
                Find.WindowStack.Add(new Dialog_KMHLogViewer());
            }

            _measuredH = listing.CurHeight + 4f;
            listing.End();
            Widgets.EndScrollView();
        }

        private static void ButtonThenGap(Listing_Standard listing, string label, System.Action onClick)
        {
            if (listing.ButtonText(label)) onClick();
            listing.Gap(6f);
        }
    }
}