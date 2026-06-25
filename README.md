# KMH Patch

The client mod for **RimWorld Together** - a full player-driven economy in one
KMH tab: marketplace, auctions, treasuries, a quest board, guilds, reputation,
production sites, server-wide events, and a Server Standings hub. It pairs with
the **[KMH Server Addon](../KMH-Server-Addon)** and lights up on its own when you
join a KMH server; on any other server it stays dormant, so it's safe to leave
enabled everywhere.

## RWT version

Recommended **RWT 26.6.9.1** — KMH v1.1.0 is built and tested around it. It also
runs on **RWT 26.5.24.1** (the older RWT generation); the mod auto-detects which
one you're on and loads the matching build, nothing to configure.

**RWT 26.6.23.1 and newer is experimental / compatibility-in-progress — don't
update to it yet.** If Steam auto-updated RimWorld Together past 26.6.9.1,
downgrade in-game: `Mod Options → RimWorld Together → Change Version → 26.6.9.1`.

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
  who earn XP, and route the output to treasury, marketplace, or colony.
- **Server Standings** - a progression hub with player, guild,
  member-contribution, colony, colonist, trade, contract, battle, site, and
  reputation boards, a season archive, and full player/colonist profiles.
- **Offline mail** - outcomes that land while you're away (auction won or sold,
  want filled, quest approved) arrive as letters the next time you log in.
- **Discord link** - tie your account to Discord with one command; on servers
  running the bridge you can trade from Discord too.
- **Config sync** - on servers that enforce mod configs, your settings sync to
  the server profile with your originals backed up, a panel showing what's
  locked, and one-click restore.
- **KMH API transport** *(optional, experimental, off by default)* - enable
  **Use KMH API transport** in mod settings to talk to a KMH server over its
  own port instead of RWT chat; it falls back to chat if that's unreachable,
  and the KMH tab shows which path is live.

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
