using KMHPatch.Diagnostics;
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

        private static readonly string[] Highlights =
        {
            "Talk to your server without leaving the game: the new Communications hub puts server chat, guild chat and direct messages in one place, with unread badges on the KMH tab so you notice a message without watching the chat window.",
            "Player mail: send another player a letter with silver, items or full-condition gear attached. Nothing is ever lost - unclaimed attachments come back to you automatically, and you can recall anything they haven't read yet.",
            "Choose how loud KMH is: per-channel notification levels (silent / toast / toast + sound) for server, guild and DMs, plus a one-click mute in the hub and in mod settings.",
            "Blocking and moderation: block a player to stop seeing their messages anywhere, and server staff can remove a message for everyone.",
            "Your off-map wealth now counts toward raids. Silver and goods parked in your treasury, guild vault, listings, auctions, wants, quests and mail attachments feed RimWorld's threat scaling, so stockpiling off-map is no longer a way to farm safely. Servers control this.",
            "Deposits are harder to abuse: a deposit that was confirmed and then rolled back by loading an older save is now undone on the server too, so goods can't be in your colony and your vault at once.",
            "Discord: KMH chat can mirror to a Discord channel in either direction, with linked accounts posting under their real in-game name.",
            "Servers can be extended properly now: a documented extension SDK with events, veto hooks and per-extension storage, so server owners can add their own rules without forking KMH.",
            "Safer upgrades: config files migrate automatically with a recorded report, a damaged config is restored from backup instead of silently reverting to defaults, and the server refuses to move value it cannot write to disk.",
            "Lots of quieter fixes: item stacks with saved condition are no longer mis-counted when filling a want, expired listings and quests return goods reliably, and the server recovers cleanly from a failed save instead of reporting success."
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
            if (_chainWelcome && KMHPatchMod.Settings?.ShowWelcomeOnLaunch == true)
                Find.WindowStack.Add(new Dialog_KMHWelcome());
        }

        protected override void DrawContents(Rect rect)
        {
            float y = 0f;

            // Measured, not fixed: a wrapped header loses its lower line and offsets every control below it.
            string title = $"What's new in {Constants.DisplayName} v{KmhProtocol.DisplayVersion}";
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            float titleH = Mathf.Max(40f, Text.CalcHeight(title, rect.width));
            Widgets.Label(new Rect(0f, y, rect.width, titleH), title);
            y += titleH + 4f;

            Widgets.DrawLineHorizontal(0f, y, rect.width);
            y += 10f;

            const string subtitle = "Talk to your server, send mail, and a tighter economy.";
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

            float contentHeight = 0f;
            foreach (string highlight in Highlights)
                contentHeight += Text.CalcHeight("- " + highlight, textW) + 8f;

            Widgets.BeginScrollView(viewArea, ref _scroll,
                new Rect(0f, 0f, innerWidth, Mathf.Max(contentHeight, viewArea.height)));

            float yy = 0f;
            foreach (string highlight in Highlights)
            {
                string line = "- " + highlight;
                float lineHeight = Text.CalcHeight(line, textW);
                Widgets.Label(new Rect(8f, yy, textW, lineHeight), line);
                yy += lineHeight + 8f;
            }

            Widgets.EndScrollView();

            // Buttons share the row and shrink; fixed widths drove the centring maths to a negative left edge.
            const float linkHeight = 36f, gap = 12f;
            float linkY  = rect.height - linkRowH - okRowH + 4f;
            float linkW  = Mathf.Min(250f, (rect.width - gap - 16f) / 2f);
            float left   = (rect.width - linkW * 2f - gap) / 2f;

            DrawLinkButton(new Rect(left, linkY, linkW, linkHeight), "KMH Discord / full notes", Constants.DiscordUrl);
            DrawLinkButton(new Rect(left + linkW + gap, linkY, linkW, linkHeight), "KMH on GitHub", Constants.GitHubUrl);

            float okW = Mathf.Min(160f, rect.width - 16f);
            Rect okRect = new Rect((rect.width - okW) / 2f, rect.height - 40f, okW, 36f);
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