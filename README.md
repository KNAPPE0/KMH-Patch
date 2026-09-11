# KMH Patch

The client mod for **RimWorld Together** - a full player-driven economy in one
KMH tab: marketplace, auctions, treasuries, a quest board, guilds, reputation,
production sites, server-wide events, and a Server Standings hub. It pairs with
the **[KMH Server Addon](../KMH-Server-Addon)** and lights up on its own when you
join a KMH server; on any other server it stays dormant, so it's safe to leave
enabled everywhere.

## RWT version

KMH v1.3.0 is verified against the nine most recent RWT releases:

| RWT release | Status |
| --- | --- |
| 26.8.31.1, 26.8.16.1 (1), 26.8.16.1, 26.8.9.1, 26.7.25.1 | supported |
| 26.6.23.1, 26.6.9.1, 26.6.8.1 | supported |
| 26.5.24.1 | supported — oldest |
| 26.4.18.1 and older | **not supported** |

The mod ships all three client builds and auto-detects which RWT generation
you're on, so there is nothing to configure. Below 26.5.24.1 RWT moved a core
network type, which makes those releases a different generation rather than a
variation — KMH refuses to start there and says so, leaving RWT itself running
normally.

**On RWT 26.6.23.1 and newer, RWT chat is not a reliable KMH carrier — the KMH
API transport (now on by default) is the recommended/required path there.** Make
sure the server runs KMH v1.3.0 with its API transport enabled (also the
default).

Update the **KMH Patch** and the **KMH Server Addon** together so both sides
match.

## What you get

Everything lives in one **KMH** tab with a live dashboard of your silver,
quests, listings, guild, and Discord link:

- **Marketplace** - sell from a caravan (item, quantity, price, public or
  guild-only), browse with search and category filters, and buy with treasury
  silver. Your goods sit in escrow until they sell or you cancel.
- **Auctions & want board** - post timed auctions (bid, buyout, anti-snipe) or
  a want-to-buy order with escrowed silver for others to fill.
- **Living World** - a dashboard of active server events and co-op/competitive
  global quests you can contribute toward for shared rewards.
- **Treasury** - personal and guild vaults for silver and items with a live
  activity feed. Deposit silver and stored colony goods straight from your
  stockpiles and shelves - no caravan needed - and withdraw by caravan or drop
  pod.
- **Quest board** - deliver, bounty, escort, defend, hunt, build, and custom
  quests. Bounty silver is escrowed up front; hunt and build quests complete
  themselves, the rest use a proof-and-review flow.
- **Reputation** - your quest behaviour builds a trust score shown as a badge
  next to your name on every board.
- **Guild Hall** - ranks, invites, buyable perks, a shared vault with per-rank
  daily limits, an MOTD, and alliance/rivalry diplomacy that gates guild-only
  visibility.
- **Sites** - build production sites on the world map, staff them with workers
  who earn XP, and route the output to treasury, marketplace, or colony. Each
  site has an archetype (Farmland, Quarry, Woodland, Ranch, Roadworks or Custom)
  that decides which colonist skill its work uses and pays a specialization
  bonus when it matches what you produce. Spend its slots on production, housing
  or storage - storage holds output for collection instead of delivering every
  cycle. A damaged site produces less but never nothing, and repairs are priced
  on the damage actually undone.
- **Roadworks** - a site archetype that lays real, permanent roads across the
  world: Trail, then Road, then Highway. You pay per new segment, so crossing
  someone else's road is free, and cancelling returns the silver for everything
  still unbuilt.
- **The Frontier** - the world opens its own objectives. Restore a derelict ruin
  alongside everyone else, and the location goes to whoever contributed most,
  earliest - yours or your guild's, your choice at claim time. A captured place
  arrives as bare infrastructure: pick what it produces once, for free, and it
  becomes a normal site. Your outposts and the claims you've won both show in
  Standings, and losing a place never erases the record of winning it.
- **Server Standings** - a progression hub with player, guild,
  member-contribution, colony, colonist, trade, contract, battle, site, and
  reputation boards, a season archive, and full player/colonist profiles.
- **Communications** - one window for live chat and mail. Server-wide, guild, and
  private channels keep recent history across a reconnect, and the KMH tab shows
  an unread count and glows when something is waiting. Mail reaches players
  whether they're online or not - pick the recipient from the roster, attach
  silver, items, or gear, and reply straight from the message. Attachments are
  held safely until accepted, come back on their own if never opened, and you can
  recall one any time. Block anyone you'd rather not hear from, and set how loud
  each channel is (silent, popup, or popup with sound) or mute the lot.
- **Offline notifications** - outcomes that land while you're away (auction won
  or sold, want filled, quest approved) arrive as letters the next time you log
  in.
- **Discord link** - tie your account to Discord with one command; on servers
  running the bridge you can trade from Discord too, and KMH chat can be mirrored
  to a Discord channel with messages tagged by where they came from.
- **Config sync** - on servers that enforce mod configs, your settings sync to
  the server profile with your originals backed up, a panel showing what's
  locked, and one-click restore.
- **KMH API transport** *(on by default — the recommended path for newer RWT
  versions)* - talks to a KMH server over its own port instead of RWT chat
  whenever the server advertises it; falls back to chat if that's unreachable,
  and the KMH tab shows which path is live. Toggle in mod settings.

Icons are optional - drop `.png`/`.dds` files in `Textures/KMHPatch/UI/` and
the buttons pick them up; without them everything still works as text.

## Install & update

This folder is a RimWorld mod. Copy it into RimWorld's `Mods/` folder, enable
**KMH Patch** in the mod list **below** RimWorld Together, and restart. The KMH
tab appears on its own when you join a KMH server. To update, replace the folder
(or update from the Steam Workshop) and restart — nothing of yours to wipe.

## Extending

Client-side extensions can add their own UI and behaviour through the KMH
client SDK without forking this mod. See [EXTENSIONS.md](EXTENSIONS.md) and the
example in `Templates/ClientExtension/`.

## License

See [LICENSE](LICENSE).
