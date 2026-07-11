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
            "v1.2.0 makes servers feel alive by default: world events and global quests now roll on their own - markets swing, tax holidays hit, bounties post, and global weather (auroras, eclipses, cold snaps, heat waves) sweeps every colony at once.",
            "Safer deposits: treasury deposits now finalize only after you SAVE your game. If you disconnect before saving, the deposit is rolled back on both sides - so the old 'deposit then lose connection' silver-duplication can't happen. You'll see a 'pending until saved' note until it's locked in.",
            "Sites got a real catalog: pick outputs from a curated, tiered list (Basic / Refined / Advanced, with a disabled-by-default Rare/Tech tier) instead of a dev-mode item list - no more weapon/tech/gene printers. Workers speed up cycles, skill raises the amount (both tier-capped), a Site with no workers now clearly shows 'Paused', and owners can work their own Site.",
            "KMH API transport is now on by default - the recommended path for newer RWT versions. It authenticates over your verified session and falls back to chat automatically.",
            "New to KMH? The KMH tab now has a 'How KMH works' tutorial explaining every system - treasury, marketplace, auctions, wants, quests, sites, guilds, events, and standings. Opening the tab also refreshes everything at once.",
            "Guild invites got friendly: pick players from a list (online or offline - offline invites wait for them), and invitees get a clear notification with one-click accept/decline in the Guild Hall.",
            "Joining a guild is one click too: browse every guild on the server with open/invite-only tags instead of typing names.",
            "Anti-cheat: servers can auto-reset your KMH treasury when you start a new save, closing a silver-farming exploit (owner opt-in).",
            "Custom sites are now priced by output, so nobody can game an item's value for cheap, fast, high-yield sites.",
            "Dashboard: systems the server has turned off now read 'disabled by server' instead of loading forever."
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
                "A living world by default, fairer economy, tighter exploit guards.");

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