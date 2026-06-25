using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Main-menu KMH info panel for links, diagnostics, and client settings.
    public class Dialog_KMHAbout : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(560f, 420f);

        public Dialog_KMHAbout()
        {
            doCloseButton = true;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
        }

        protected override void DrawContents(Rect inRect)
        {
            // Leave room for the close button.
            Rect content = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 45f);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(content);

            Text.Font = GameFont.Medium;
            listing.Label($"{Constants.DisplayName}");
            Text.Font = GameFont.Small;

            listing.Gap(6f);
            listing.Label(
                "KMH is a feature pack that adds client-side improvements on top of " +
                "the official RimWorld Together mod. It runs as a separate patch - " +
                "no RimWorld Together files are modified."
            );

            listing.GapLine(12f);

            listing.Label($"Version: {typeof(Dialog_KMHAbout).Assembly.GetName().Version}");
            listing.Label($"Package: {Constants.PackageId}");

            listing.Gap(12f);

            // External links and KMH utility actions.
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

            if (listing.ButtonText("Open KMH log folder"))
            {
                // Opens the log folder in the OS file manager.
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