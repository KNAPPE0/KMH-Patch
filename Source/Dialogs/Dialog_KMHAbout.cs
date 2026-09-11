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

        protected override void DrawContents(Rect inRect)
        {
            Rect content = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 45f);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(content);

            Text.Font = GameFont.Medium;
            listing.Label($"{Constants.DisplayName}");
            Text.Font = GameFont.Small;

            listing.Gap(6f);
            listing.Label(
                "KMH is a community feature pack for the official RimWorld Together mod: " +
                "a marketplace, auctions and a want board, personal and guild treasuries, " +
                "quests, guilds, production sites, roadworks, frontier outposts, chat and " +
                "player mail, and server-wide events. It runs as a separate patch - no " +
                "RimWorld Together files are modified."
            );

            listing.GapLine(12f);

            listing.Label($"Version: {typeof(Dialog_KMHAbout).Assembly.GetName().Version}");
            listing.Label($"Package: {Constants.PackageId}");

            listing.Gap(12f);

            const float gap = 6f;

            if (listing.ButtonText("Patch GitHub (client mod)"))
            {
                Application.OpenURL(Constants.GitHubUrl);
            }
            listing.Gap(gap);

            if (listing.ButtonText("Server Addon GitHub"))
            {
                Application.OpenURL(Constants.GitHubServerUrl);
            }
            listing.Gap(gap);

            if (listing.ButtonText("Open Discord server"))
            {
                Application.OpenURL(Constants.DiscordUrl);
            }
            listing.Gap(gap);

            if (listing.ButtonText("Show welcome dialog again"))
            {
                Find.WindowStack.Add(new Dialog_KMHWelcome());
            }
            listing.Gap(gap);

            if (listing.ButtonText("What's new in this version"))
            {
                Find.WindowStack.Add(new Dialog_KMHWhatsNew());
            }
            listing.Gap(gap);

            if (listing.ButtonText("Open KMH log folder"))
            {
                Application.OpenURL(KmhLog.LogFolderPath);
            }
            listing.Gap(gap);

            if (listing.ButtonText("View KMH log in-game"))
            {
                Find.WindowStack.Add(new Dialog_KMHLogViewer());
            }

            listing.End();
        }
    }
}