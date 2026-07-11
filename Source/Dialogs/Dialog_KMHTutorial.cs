using System.Collections.Generic;
using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Plain-language guide to every KMH system: what it is, how to use it, and the gotchas. Scrollable, self-contained.
    public class Dialog_KMHTutorial : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(680f, 640f);

        private Vector2 _scroll;
        private static readonly (string Title, string Body)[] Sections =
        {
            ("Getting started",
             "KMH adds cross-player systems to a RimWorld Together server: a shared economy, guilds, quests, a living " +
             "world, and player standings. Everything is reached from the KMH tab at the bottom of the screen. Features " +
             "only appear when you're connected to a server running the KMH Server Addon - a plain RWT server shows none " +
             "of them. Opening the tab pulls fresh data automatically."),

            ("Treasury - your server bank",
             "Your Treasury is a server-side vault for silver and items, separate from your colony. Deposit from a " +
             "selected caravan or your colony; withdraw to drop it back into your colony. Complex items (weapons, " +
             "apparel, minified buildings, modded gear) keep their exact state - damage, taint, quality, material and " +
             "comp data survive the round-trip, and clean/tainted or damaged/undamaged stacks never merge. Plain " +
             "resources stay compact. It's the backbone of trading, and you never travel to anyone - all transfers " +
             "happen on the server ledger. If you're in a guild you can also contribute to the guild vault (silver)."),

            ("Marketplace - buy & sell",
             "A server-wide board. Post an item (it's escrowed from your Treasury), set a unit price, and anyone can buy " +
             "it - silver goes to your Treasury minus the house tax, and the buyer receives the item in theirs. Cancel a " +
             "listing to get the exact item back. Guild members can post guild-only listings that only their guild and " +
             "allies see. No guild is required to trade on the public board."),

            ("Auctions - highest bidder wins",
             "List an item for timed bidding. Bidders escrow their silver up front; outbid players are refunded " +
             "automatically. When it ends, the winner gets the item and you get the silver (minus tax); no bids returns " +
             "the item to you. A late bid extends the close (anti-snipe)."),

            ("Want Board - post what you need",
             "Post a buy-order: item, quantity, and price-per-unit. You escrow the full silver up front. Sellers fulfill " +
             "it from their Treasury and get paid; unfilled escrow is refunded when it expires. Great for standing orders " +
             "you don't want to babysit."),

            ("Quests & global quests",
             "Post player quests with silver/item bounties for others to claim and complete. Server-wide GLOBAL quests " +
             "(hunt / build / deliver) also roll on their own - progress is tracked automatically: hunts count your kills, " +
             "builds count structures you raise, deliveries you hand in from the Quest Board. Rewards are real silver from " +
             "the server's house pool, shared among contributors."),

            ("Sites - custom production",
             "Build a production Site on the world map that generates a chosen resource over time. Pick the output from a " +
             "curated catalog: outputs are sorted into tiers (Basic, Refined, Advanced, and a disabled-by-default Rare/Tech " +
             "tier) - a Site can't produce weapons, apparel, tech, drugs, genes, or relics. Each output shows its tier, the " +
             "relevant skill, its max amount, and estimated cost + cycle. Workers make cycles FASTER; worker skill raises the " +
             "AMOUNT (each capped by the output's tier). A Site with no workers is Paused - the owner counts as a worker so a " +
             "solo/private Site still produces. Set where rewards go (Treasury, colony, or auto-list on the marketplace); if a " +
             "colony drop can't happen it falls back to your Treasury so nothing is lost."),

            ("Guilds",
             "Create or join a guild for a shared identity, a guild vault, perks, alliances, and guild-only trading. " +
             "Officers invite players from a picker (online or offline - offline invites wait for them); you accept or " +
             "decline invites in the Guild Hall. Join open guilds in one click from the guild picker. Ranks gate who can " +
             "invite, withdraw, buy perks, and manage settings."),

            ("Living world - events & weather",
             "The server periodically fires world events: tax holidays, market booms/crashes, resource shortages, double " +
             "worker XP, house stipends, bounties, and GLOBAL WEATHER (auroras, eclipses, cold snaps, heat waves and more) " +
             "that sweep every colony at once. Watch the announcements - some events are the best time to sell or hunt."),

            ("Server Standings",
             "Leaderboards and record boards: top players and guilds, plus colony/colonist/trade/contract/battle/site and " +
             "reputation records. Reputation rises as you complete quests and honor contracts - it shows as a trust badge " +
             "next to your name in trading dialogs."),

            ("Discord linking",
             "Link your in-game account to Discord (button in the KMH tab, or /kmh link) to trade and check status from the " +
             "server's Discord bot, and - on servers that enable it - get a guild role that mirrors your rank."),

            ("Debug logging (for support)",
             "If an owner is helping you with a problem, enable 'Send debug logs to the server' in Mod Options - your KMH " +
             "log lines mirror to the server (timestamped, rate-limited) so they can see what happened. A server can also " +
             "turn this on for everyone while diagnosing. It's off by default and only covers KMH activity."),
        };

        public Dialog_KMHTutorial()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;
        }

        protected override void DrawContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(0f, 0f, rect.width, 34f), "KMH - How everything works");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            float top = 40f;
            float bottom = 44f;
            Rect view = new Rect(0f, top, rect.width, rect.height - top - bottom);

            // Measure once so the scroll content fits.
            float innerW = view.width - 20f;
            float h = 0f;
            List<float> heights = new List<float>(Sections.Length);
            foreach ((string title, string body) in Sections)
            {
                float bh = Text.CalcHeight($"<b>{title}</b>\n{body}", innerW - 8f) + 14f;
                heights.Add(bh);
                h += bh;
            }

            Widgets.BeginScrollView(view, ref _scroll, new Rect(0f, 0f, innerW, h));
            float y = 0f;
            for (int i = 0; i < Sections.Length; i++)
            {
                (string title, string body) = Sections[i];
                Rect card = new Rect(0f, y, innerW, heights[i] - 6f);
                if (i % 2 == 0) Widgets.DrawLightHighlight(card);
                Widgets.Label(new Rect(6f, y + 4f, innerW - 12f, heights[i] - 12f),
                    $"<b><color=#F4B83C>{title}</color></b>\n{body}");
                y += heights[i];
            }
            Widgets.EndScrollView();

            Rect close = new Rect((rect.width - 160f) / 2f, rect.height - 36f, 160f, 32f);
            if (Widgets.ButtonText(close, "Got it")) Close();
        }
    }
}
