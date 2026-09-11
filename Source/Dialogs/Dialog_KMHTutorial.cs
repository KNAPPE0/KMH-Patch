using System.Collections.Generic;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    public class Dialog_KMHTutorial : Window_KMHBase
    {
        protected override bool ClosesOnSessionEnd => false;

        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(680f, 640f);

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
             "selected caravan or straight from your colony; withdraw to drop it back home. Complex gear (weapons, " +
             "apparel, minified buildings, quality or modded items) keeps its exact state - damage, taint, quality, " +
             "material and comp data survive the round-trip, and different states never merge. Plain resources stay " +
             "compact. Tip: put \"KMH\" in a stockpile's name (e.g. \"KMH Depot\") and withdrawals always land there. " +
             "It's the backbone of trading - you never travel to anyone; all transfers happen on the server ledger. In " +
             "a guild you can also contribute to the guild vault (silver)."),

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
             "AMOUNT (each capped by the output's tier). A Site needs real colonists working it: send a caravan to the tile and " +
             "assign a pawn, or it stays Paused. Some servers configure the owner to count as a worker automatically. " +
             "Set where rewards go (Treasury, colony, or auto-list on the marketplace); if a " +
             "colony drop can't happen it falls back to your Treasury so nothing is lost."),

            ("Site archetypes, buildings & condition",
             "When you build a Site you pick an archetype - Farmland, Quarry, Woodland, Ranch, Roadworks or Custom. It names the " +
             "site and decides which colonist skill the work uses; Custom keeps whatever the output implies. Sites you " +
             "already own read as Custom and are unchanged. Each site has a couple of building slots (more at higher " +
             "tiers): production for more output, housing for more workers, storage so the site holds what it makes until " +
             "you collect it in bulk - anything that won't fit still arrives in your Treasury. Demolishing refunds nothing. " +
             "A damaged site produces less but never nothing, and you can repair it from the site's building window for a " +
             "price based on the damage. Nothing lowers condition on its own."),

            ("Roadworks - building roads",
             "From a Roadworks site you can plan real roads on the world map. Pick a destination and KMH routes it, quotes " +
             "the cost before you commit, and builds Trail, Road or Highway depending on your site's tier. Stretches that " +
             "already have road aren't charged for again, and cancelling returns the silver for anything still unbuilt. " +
             "KMH only ever writes roads it planned and puts back whatever it replaced if one is removed, so roads from " +
             "worldgen or another mod are left alone."),

            ("Frontier outposts",
             "The server opens objectives out in the world - the first restores a derelict ruin. Contested and restored " +
             "locations show up in your Sites window beside your own, with what they are, their condition, and how long " +
             "the claim window has left; on the world map they carry their own marker. When the server says you earned " +
             "one, a Claim button takes it for yourself or your guild. Captures are permanent history: losing the place " +
             "later never removes it from your record."),

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

            ("Communications - chat & mail",
             "One window for talking to the server: a server-wide channel, your guild channel, and private DMs, plus your " +
             "mailbox. The KMH tab shows an unread count and glows when something is waiting, and recent chat survives a " +
             "reconnect. Mail reaches players whether they are online or not - pick the recipient from the roster, add a " +
             "subject and message, and reply straight from what you received."),

            ("Sending silver, items & gear by mail",
             "Mail can carry silver, stacked items, and full-state gear straight from your treasury. The goods are held " +
             "safely the moment you send: the recipient accepts them into their own treasury, or declines and they come " +
             "back to you. Nothing is ever lost - an attachment nobody opens returns to you automatically, and you can " +
             "recall your own unopened mail at any time. Some servers charge a small fee to attach goods."),

            ("Keeping it civil",
             "Click any chat message to block that player (you stop seeing their messages and their mail), or unblock them " +
             "later from the Communications window. Set how loud each channel is - silent, popup, or popup with sound - or " +
             "mute everything, in the KMH mod settings or the button in the Communications window. Server staff can remove " +
             "messages."),

            ("Discord linking",
             "Link your in-game account to Discord (button in the KMH tab, or /kmh link) to trade and check status from the " +
             "server's Discord bot, and - on servers that enable it - get a guild role that mirrors your rank. Servers can " +
             "also mirror KMH chat to a Discord channel; relayed messages are tagged so you know where they came from."),

            ("Debug logging (for support)",
             "If an owner is helping you with a problem they can ask for your KMH log. Nothing is sent unless you agree: " +
             "when a server asks, you get a one-time prompt with 'Share for this session' or 'Not now', and that choice " +
             "is dropped the moment you disconnect. There's also a standing opt-in in Mod Options if you'd rather share " +
             "automatically. While sharing is on, the KMH tab says so, and 'Stop sharing my KMH log' under Advanced & " +
             "tools ends it immediately. Only KMH log lines are sent - never chat, saves, or system information."),
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
            const string title = "KMH - How everything works";
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            // Measured, not fixed: a wrapped title loses its lower line and offsets everything below it.
            float titleH = Mathf.Max(34f, Text.CalcHeight(title, rect.width));
            Widgets.Label(new Rect(0f, 0f, rect.width, titleH), title);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            float top    = titleH + 6f;
            float bottom = 44f;
            Rect view = new Rect(0f, top, rect.width,
                Mathf.Max(DialogLayout.MinBodyHeight, rect.height - top - bottom));

            // ONE width for measure and draw: measuring wider than the draw clips the last line of every section.
            float innerW = Mathf.Max(1f, view.width - DialogLayout.ScrollbarReserveWidth);
            float textW  = Mathf.Max(1f, innerW - 12f);

            float h = 0f;
            List<float> heights = new List<float>(Sections.Length);
            foreach ((string t, string body) in Sections)
            {
                float bh = Text.CalcHeight($"<b>{t}</b>\n{body}", textW) + 14f;
                heights.Add(bh);
                h += bh;
            }

            Widgets.BeginScrollView(view, ref _scroll, new Rect(0f, 0f, innerW, Mathf.Max(h, view.height)));
            float y = 0f;
            for (int i = 0; i < Sections.Length; i++)
            {
                (string t, string body) = Sections[i];
                Rect card = new Rect(0f, y, innerW, heights[i] - 6f);
                if (i % 2 == 0) Widgets.DrawLightHighlight(card);
                Widgets.Label(new Rect(6f, y + 4f, textW, heights[i] - 12f),
                    $"<b><color=#F4B83C>{t}</color></b>\n{body}");
                y += heights[i];
            }
            Widgets.EndScrollView();

            float closeW = Mathf.Min(160f, rect.width - 16f);
            Rect close = new Rect((rect.width - closeW) / 2f, rect.height - 36f, closeW, 32f);
            if (Widgets.ButtonText(close, "Got it")) Close();
        }
    }
}
