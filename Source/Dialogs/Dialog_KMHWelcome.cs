using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Two-step welcome flow shown on first launch.
    //
    // Step 1: KMH credits + KMH project links. Step 2: official RimWorld Together credits + RWT project links.
    //
    // RWT credits come second on purpose - KMH is what the player just installed, but Nova's team is the reason it
    // exists; both screens get equal weight.
    //
    // Built on Verse.Window + Find.WindowStack.Add, so it has zero dependency on RWT's UI helper classes.
    public class Dialog_KMHWelcome : Window_KMHBase
    {
        public enum Step { KMH, OfficialRWT }

        public override Vector2 InitialSize => new Vector2(560f, 440f);

        private readonly Step   _step;
        private readonly string _title;
        private readonly string _description;

        // Default ctor maps to step 1; the OK button on step 1 pushes step 2.
        public Dialog_KMHWelcome() : this(Step.KMH) { }

        public Dialog_KMHWelcome(Step step)
        {
            _step = step;

            if (step == Step.KMH)
            {
                _title       = $"Welcome to {Constants.DisplayName}";
                _description =
                    "This is a community feature pack that adds extra dialogs, " +
                    "branding, and integrations on top of the official RimWorld " +
                    "Together mod. It runs as a clean-room patch - no RWT files " +
                    "are modified or bundled.";
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

            // Standard popup behavior. We handle accept/cancel manually so the OK button can chain from step 1 to
            // step 2
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = 0f;

            Text.Font   = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(0f, y, rect.width, 40f), _title);
            y += 44f;

            Widgets.DrawLineHorizontal(0f, y, rect.width);
            y += 12f;

            Text.Font   = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(8f, y, rect.width - 16f, 70f), _description);
            y += 80f;

            const float btnW = 200f;
            const float btnH = 38f;
            const float gap  = 10f;
            float       btnY = y;

            if (_step == Step.KMH)
            {
                DrawLinkButton(rect, ref btnY, btnW, btnH, gap, "KMH Discord",          Constants.DiscordUrl);
                DrawLinkButton(rect, ref btnY, btnW, btnH, gap, "KMH Patch (GitHub)",   Constants.GitHubUrl);
                DrawLinkButton(rect, ref btnY, btnW, btnH, gap, "KMH Server (GitHub)",  Constants.GitHubServerUrl);
                DrawLinkButton(rect, ref btnY, btnW, btnH, gap, "KMH Steam Workshop",   Constants.SteamWorkshopUrl);
            }
            else
            {
                DrawLinkButton(rect, ref btnY, btnW, btnH, gap, "Official RWT Discord",     Constants.OfficialRwtDiscordUrl);
                DrawLinkButton(rect, ref btnY, btnW, btnH, gap, "Official RWT GitHub",      Constants.OfficialRwtGitHubUrl);
                DrawLinkButton(rect, ref btnY, btnW, btnH, gap, "Official Steam Workshop",  Constants.OfficialRwtSteamWorkshopUrl);
                DrawLinkButton(rect, ref btnY, btnW, btnH, gap, "Support RWT (Ko-fi)",      Constants.OfficialRwtKofiUrl);
            }

            // OK at the bottom: step 1 chains into step 2, step 2 closes the flow.
            Rect okRect = new Rect((rect.width - 150f) / 2f, rect.height - 44f, 150f, 38f);
            if (Widgets.ButtonText(okRect, "OK"))
            {
                Close();
                if (_step == Step.KMH)
                {
                    Find.WindowStack.Add(new Dialog_KMHWelcome(Step.OfficialRWT));
                }
            }

            // Reset IMGUI globals so we don't leak alignment into the next thing.
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawLinkButton(Rect rect, ref float y, float w, float h, float gap, string label, string url)
        {
            Rect btn = new Rect((rect.width - w) / 2f, y, w, h);
            if (Widgets.ButtonText(btn, label))
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
            y += h + gap;
        }
    }
}
