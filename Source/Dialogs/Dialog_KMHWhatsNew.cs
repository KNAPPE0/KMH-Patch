using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Changelog popup shown once after the player updates.
    public class Dialog_KMHWhatsNew : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(580f, 520f);

        private Vector2 _scroll;

        private static readonly string[] Highlights =
        {
            "v1.1.1 is a small client hotfix for smoother setup, reconnects, and server matching.",
            "KMH Servers list: the KMH tab now remembers servers you joined and shows their last seen KMH/RWT versions.",
            "Guilds: players can create a guild from the Guild Hall without using a chat command.",
            "Discord linking: use the Link Discord button to get a one-time code instead of typing a command.",
            "Reconnect fix: KMH now resumes its own transport properly after reconnecting instead of silently falling back to chat-only.",
            "Version notices are clearer when the client patch and server addon do not match.",
            "Experimental API transport is safer on the server side. It is local-only and authenticated by default unless the owner changes it."
        };

        // When shown on the main menu we chain into the welcome credits on close, so all three appear in sequence
        // instead of stacking. The About-panel "what's new" button opens it with chaining off.
        private readonly bool _chainWelcome;

        public Dialog_KMHWhatsNew(bool chainWelcome = false)
        {
            _chainWelcome = chainWelcome;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            closeOnClickedOutside = false;
        }

        public override void PostClose()
        {
            base.PostClose();
            if (_chainWelcome && KMHPatchMod.Settings?.ShowWelcomeOnLaunch == true)
                Find.WindowStack.Add(new Dialog_KMHWelcome());
        }

        protected override void DrawContents(Rect rect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(
                new Rect(0f, y, rect.width, 40f),
                $"What's new in {Constants.DisplayName} v{KmhProtocol.BuildVersion}");

            y += 44f;

            Widgets.DrawLineHorizontal(0f, y, rect.width);
            y += 10f;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(
                new Rect(0f, y, rect.width, 24f),
                "Small hotfix, cleaner player setup, safer transport defaults.");

            y += 30f;

            const float buttonRowH = 44f;
            const float linkRowH = 44f;

            Rect viewArea = new Rect(0f, y, rect.width, rect.height - y - buttonRowH - linkRowH - 8f);
            float innerWidth = viewArea.width - 16f;

            Text.Anchor = TextAnchor.UpperLeft;

            float contentHeight = 0f;
            foreach (string highlight in Highlights)
            {
                contentHeight += Text.CalcHeight("- " + highlight, innerWidth - 12f) + 8f;
            }

            Widgets.BeginScrollView(viewArea, ref _scroll, new Rect(0f, 0f, innerWidth, contentHeight));

            float yy = 0f;
            foreach (string highlight in Highlights)
            {
                string line = "- " + highlight;
                float lineHeight = Text.CalcHeight(line, innerWidth - 12f);

                Widgets.Label(new Rect(8f, yy, innerWidth - 12f, lineHeight), line);
                yy += lineHeight + 8f;
            }

            Widgets.EndScrollView();

            float linkY = rect.height - buttonRowH - linkRowH;

            const float linkWidth = 250f;
            const float linkHeight = 36f;
            const float gap = 12f;

            float left = (rect.width - linkWidth * 2f - gap) / 2f;

            DrawLinkButton(new Rect(left, linkY, linkWidth, linkHeight), "KMH Discord / full notes", Constants.DiscordUrl);
            DrawLinkButton(new Rect(left + linkWidth + gap, linkY, linkWidth, linkHeight), "KMH on GitHub", Constants.GitHubUrl);

            Rect okRect = new Rect((rect.width - 160f) / 2f, rect.height - 40f, 160f, 36f);
            if (Widgets.ButtonText(okRect, "Got it"))
            {
                Close();
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawLinkButton(Rect rect, string label, string url)
        {
            if (!Widgets.ButtonText(rect, label)) return;

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