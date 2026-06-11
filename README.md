# KMH Patch

The client mod for **RimWorld Together** that adds a complete player-driven
economy to your multiplayer colony. It pairs with the
**[KMH Server Addon](../KMH-Server-Addon)** and lights up automatically when you
join a KMH-enabled server - on any other server it stays dormant, so it's safe
to leave enabled everywhere.

Works with both current RimWorld Together versions (26.5.24.1 and 26.6.9.1+);
the mod detects which one you have and adapts automatically.

## What you get

Everything lives in one **KMH** tab with a live dashboard of your silver,
quests, listings, guild, and Discord link:

- **Marketplace** - sell from a caravan (item, quantity, price, public or
  guild-only), browse with search and category filters, buy with treasury
  silver. Goods sit in escrow until they sell or you cancel.
- **Treasury** - personal and guild vaults for silver and items with a live
  activity feed. Withdrawals arrive by caravan or drop pod.
- **Quest board** - deliver, bounty, escort, defend, hunt, build, and custom
  quests. Bounty silver is escrowed up front; hunt and build quests complete
  themselves, the rest use a proof-and-review flow.
- **Reputation** - quest behaviour builds a trust score shown as badges next
  to names on every board.
- **Guild Hall** - ranks, invites, buyable perks, a shared vault with
  per-rank daily limits, MOTD, and alliance/rivalry diplomacy that gates
  guild-only visibility.
- **Sites** - build production sites on the world map, staff them with
  workers who earn XP, route output to treasury, marketplace, or colony.
- **Leaderboards** - player, guild, and reputation boards; click any player
  for a full stat card with per-metric ranks.
- **Discord link** - tie your account to Discord with one command; on
  servers running the bridge you can trade from Discord too.
- **Config sync** - on servers that enforce mod configs, your settings sync
  to the server profile with your originals backed up, a panel showing
  what's locked, and one-click restore.

Icons are optional - drop `.png`/`.dds` files in `Textures/KMHPatch/UI/` and
buttons pick them up; without them everything still works as text.

## Install

This folder is a RimWorld mod. Copy it into RimWorld's `Mods/` folder, enable
**KMH Patch** in the mod list **below** RimWorld Together, and restart. The KMH
tab appears on its own when you join a KMH server.

## Extending

Client-side extensions can add their own UI and behaviour via the KMH client
SDK without forking this mod. See [EXTENSIONS.md](EXTENSIONS.md) and the
example in `Templates/ClientExtension/`.

## License

See [LICENSE](LICENSE).
