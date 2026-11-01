using KMHPatch.SubProtocol;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    public class Dialog_KMHWhatsNew : Window_KMHBase
    {
        protected override bool ClosesOnSessionEnd => false;

        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(580f, 520f);

        private Vector2 _scroll;

        // This release only; each line is something a player can see or do.
        internal static string[] Highlights() => new[]
        {
            KmhText.Get("KMH_WhatsNew_Hotfix", "v1.3.1.1: settings that only sounded like display settings, such as Alpha Animals' spawn rate, are now enforced; Prepare and join handles an old Workshop copy that shares a mod's id; other players' settlements are outlined on the world map; and no colony starts empty."),
            KmhText.Get("KMH_WhatsNew_Rw17", "Plays on RimWorld 1.7, together with RimWorld Together's own 1.7 release (26.10.20.1 or newer)."),
            KmhText.Get("KMH_WhatsNew_Look", "A new Colony Nexus look: a full-screen loading screen with real progress and tips, a Colony Nexus card on the main menu and a tidier Colony Nexus tab."),
            KmhText.Get("KMH_WhatsNew_Leaderboards", "Leaderboards: 22 boards in Server Standings, and the same rankings on the Colony Nexus website, with images to share."),
            KmhText.Get("KMH_WhatsNew_Browser", "Find a server from the main menu: the Colony Nexus server browser shows each server's status, players, versions and the mods it expects, and Join connects through RimWorld Together's own login."),
            KmhText.Get("KMH_WhatsNew_Mods", "Prepare and join: one button shows what a server needs, then subscribes and downloads its Workshop mods, installs its RimWorld Together version, sets your mod list (yours is kept and can be restored), restarts RimWorld, checks what loaded and joins."),
            KmhText.Get("KMH_WhatsNew_Account", "Link a Colony Nexus Global account through Steam with a short code: one username, friends, presence, direct messages and Global Chat that follow you between servers. Global is optional - every server still plays without it."),
            KmhText.Get("KMH_WhatsNew_Federation", "Guild Federation: guilds on different servers join one federation with renown, weekly objectives and small local bonuses. Silver, items and resources never move between servers."),
            KmhText.Get("KMH_WhatsNew_Enforcement", "A server's mod settings are applied while you join, before the world loads: accept once, and reconnecting only re-checks your files - at most one restart, and none when nothing changed."),
            KmhText.Get("KMH_WhatsNew_Sites", "Sites are worked by colonists, not accounts: put several of your colonists on one site and recall them one at a time."),
            KmhText.Get("KMH_WhatsNew_Discord", "Link Discord from Account & Links. Rich Presence shows the server you are playing on (private servers stay unnamed); what you are doing in game is shown too, and Mod settings can turn that off."),
            KmhText.Get("KMH_WhatsNew_Reports", "Report a bug or an idea from the Colony Nexus tab, Advanced & tools or the main menu. You see the screenshot and the diagnostics before anything is sent, and the window says who received it."),
            KmhText.Get("KMH_WhatsNew_Performance", "Smoother online play: sending never holds up a frame, large updates are put together off the game's thread, and a large compressed update no longer drops the connection. Advanced & tools shows your frame rate, game speed and what Colony Nexus costs each frame."),
            KmhText.Get("KMH_WhatsNew_Update", "Everyone updates together: v1.3.1 speaks protocol v3, and a Patch and Server Addon from different generations say so and stay off instead of half-working."),
        };

        // Chained on close rather than opened together, or all three popups stack on the main menu.
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
            if (KMHPatchMod.Settings != null && KMHPatchMod.Settings.WhatsNewSeen != KmhProtocol.DisplayVersion)
            {
                KMHPatchMod.Settings.WhatsNewSeen = KmhProtocol.DisplayVersion;
                KMHPatchMod.SaveSettings();
            }
            if (_chainWelcome && KMHPatchMod.Settings?.ShowWelcomeOnLaunch == true && !KMHPatchMod.Settings.WelcomeSeen)
                Find.WindowStack.Add(new Dialog_KMHWelcome());
        }

        protected override void DrawContents(Rect rect)
        {
            float y = 0f;

            // Measured, not fixed: a wrapped header loses its lower line and offsets every control below it.
            string title = KmhText.Get("KMH_WhatsNew_Title", "What's new in {0} v{1}", Constants.BrandName, KmhProtocol.DisplayVersion);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            float titleH = Mathf.Max(40f, Text.CalcHeight(title, rect.width));
            Widgets.Label(new Rect(0f, y, rect.width, titleH), title);
            y += titleH + 4f;

            Widgets.DrawLineHorizontal(0f, y, rect.width);
            y += 10f;

            string subtitle = KmhText.Get("KMH_WhatsNew_Subtitle", "Connected Worlds: find a server, bring your friends, federate your guild.");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            float subH = Text.CalcHeight(subtitle, rect.width);
            Widgets.Label(new Rect(0f, y, rect.width, subH), subtitle);
            y += subH + 8f;

            const float linkRowH = 44f, okRowH = 44f;

            Rect viewArea = new Rect(0f, y, rect.width,
                Mathf.Max(DialogLayout.MinBodyHeight, rect.height - y - linkRowH - okRowH));
            float innerWidth = Mathf.Max(1f, viewArea.width - DialogLayout.ScrollbarReserveWidth);
            float textW      = Mathf.Max(1f, innerWidth - 16f);

            Text.Anchor = TextAnchor.UpperLeft;

            string[] lines = Highlights();
            float[] heights = new float[lines.Length];
            float contentHeight = 0f;
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = "- " + lines[i];
                heights[i] = Text.CalcHeight(lines[i], textW);
                contentHeight += heights[i] + 8f;
            }

            Widgets.BeginScrollView(viewArea, ref _scroll,
                new Rect(0f, 0f, innerWidth, Mathf.Max(contentHeight, viewArea.height)));

            float yy = 0f;
            for (int i = 0; i < lines.Length; i++)
            {
                Widgets.Label(new Rect(8f, yy, textW, heights[i]), lines[i]);
                yy += heights[i] + 8f;
            }

            Widgets.EndScrollView();

            // Buttons share the row and shrink; fixed widths drove the centring maths to a negative left edge.
            const float linkHeight = 36f, gap = 12f;
            float linkY  = rect.height - linkRowH - okRowH + 4f;
            float linkW  = Mathf.Min(250f, (rect.width - gap - 16f) / 2f);
            float left   = (rect.width - linkW * 2f - gap) / 2f;

            DrawLinkButton(new Rect(left, linkY, linkW, linkHeight),
                KmhText.Get("KMH_WhatsNew_FullNotes", "Colony Nexus Discord / full notes"), Constants.DiscordUrl);
            DrawLinkButton(new Rect(left + linkW + gap, linkY, linkW, linkHeight),
                KmhText.Get("KMH_WhatsNew_GitHub", "Colony Nexus on GitHub"), Constants.GitHubUrl);

            float okW = Mathf.Min(160f, rect.width - 16f);
            Rect okRect = new Rect((rect.width - okW) / 2f, rect.height - 40f, okW, 36f);
            if (Widgets.ButtonText(okRect, KmhText.Get("KMH_WhatsNew_GotIt", "Got it")))
            {
                Close();
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawLinkButton(Rect rect, string label, string url)
        {
            if (Widgets.ButtonText(rect, label)) Dialog_KMHWelcome.OpenUrl(url);
        }
    }
}