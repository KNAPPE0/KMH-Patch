# Changelog

All notable changes to the KMH: Colony Nexus client mod (called KMH Patch before v1.3.1). Versions follow the
KMH build number shared with the Colony Nexus Server Addon.

## v1.3.1.1

A client update for v1.3.1. It works with every v1.3.1 server; servers need nothing.

### rel-285

- **Gameplay settings that only sounded like display settings are now enforced.** On a server that keeps players'
  personal settings, any setting with "alpha", "offset" or "scale" in its name stayed the player's own. That caught
  Alpha Animals' spawn rate, More Faction Interaction's trader wealth, Milira's Milian modification and RimWorld of
  Magic's layered clothing offset, so each player could set them differently. Those words now count only next to a
  display word such as icon, label or UI, and every marker has to start a word, so "position" no longer matches inside
  a name like "raidComposition". If your settings still hold one of these values from an older server, the next join
  puts the server's back and asks you to restart once.
- **No restart over layout alone.** A merged settings file is compared by its values, so it only restarts RimWorld
  when a setting actually changes.
- **Prepare and join copes with an old copy that shares a mod's id.** A few 1.7 continuations, such as A RimWorld of
  Magic (Continued), Kure's class expansion and SYR Processor Framework, kept their 1.6 original's mod id. With both
  subscribed, RimWorld loads whichever it lists first, often the old one, and the server refuses the join. Colony
  Nexus took the old copy for the server's mod, so Prepare never downloaded the right one and restarted into the same
  mod list. It now downloads the copy the server uses, unsubscribes the other once the download is done (shown in the
  review before you agree), and restarts. The browser names the clash too.
- **No colony starts empty.** A gravship start sets its colonists down only on the ship's substructure. When the ship
  laid none, as A RimWorld of Magic's "Worlds Collide" does on Medieval Fantasy, every colonist was left off the map.
  Colonists a start could not place are now set down beside the landing spot.
- **The log says why settings are applied again.** When a join finds a settings file different from the server's,
  Colony Nexus now names the file, so a restart that keeps coming back can be traced.
- **Other players' settlements are outlined on the world map.** RimWorld Together now lets a player pick any icon and
  colour for their settlement, so it could look exactly like an NPC town. It now gets the same border as Colony Nexus
  places, in the "other players" colour, and follows the same switch in the Colony Nexus settings.

## v1.3.1 — Connected Worlds

**This client needs a v1.3.1 server.** The protocol is v3, and a v1.2.x or v1.3.0 server refuses it, naming both
version numbers rather than leaving you waiting. Nothing in your save needs converting.

### rel-283

- No change to the mod. Rebuilt alongside the server addon's rel-283, so both report the same build.

### rel-279 (in progress)

- **Your own mod list comes back, including after the playtest builds.** Colony Nexus kept a copy of your mod list
  before a server changed it, but compared lists in exact order. RimWorld re-sorts the list when it restarts and renames
  a Workshop copy `_steam`, so a second Prepare and join replaced the saved copy with the first server's list, and
  Restore deleted the copy without putting it back. Every playtest build from RC7 on was affected. Lists are now
  compared as sets of mods; a list you changed yourself is kept; nothing is deleted, only archived. A saved copy that
  turns out to be a server's list is set aside at start-up, and **Colony Nexus settings → Recover an older mod list…**
  offers every list that can still be recovered, including RimWorld's own backup, showing what each would switch on
  and off.
- **Your own mod settings come back, and stay back.** Settings put back while RimWorld was running were still held
  in memory by the mods, and the next settings save wrote the server's values over them again. Files put back are now
  write-protected until RimWorld restarts, and the main menu offers the restart. A server switch on v1.3.0 or older
  deleted the previous server's files, which never came back; the older backup now restores them. When no backup of
  yours was ever kept, the server's files are no longer silently treated as yours: the main menu lists them so you can
  keep them or let those mods start from their defaults.
- **Enforced settings ask twice and show what changes.** After you agree to a server's rules, Colony Nexus downloads
  its settings, writes nothing, and shows which mods' settings would be replaced, added or merged before a second
  Apply. The first dialog now also lists which mods the server's load order switches on and off. A server can no longer
  push settings you did not ask for, and a changed load order asks again instead of counting as already accepted.
- **A server's settings can no longer make RimWorld build arbitrary objects.** RimWorld constructs whatever type a
  settings file names, at every start. A profile naming anything but a mod's own settings types, carrying a DTD, or
  naming .NET types in a JSON file is refused before anything is written, and one applied by an earlier build is
  checked again at start-up and undone. Only plain settings files are unpacked, old profile copies are removed, and
  names a Windows PC would open as a device, or that differ from yours only in letter case, are refused.
- **No restart loops, and a way out.** Only an apply you agreed to in this session restarts RimWorld by itself; a
  repair, or a second restart for the same settings within 15 minutes, asks first. Mods that save their own settings
  can no longer rewrite an enforced file. If RimWorld does not reach the main menu after a server's settings were
  applied, the next start puts your own settings and mod list back and the server's consent dialog warns about it. A
  backup that cannot be trusted no longer locks you in: the main menu can release the lock and keep the current files,
  setting the damaged backup aside.
- **Prepare and join checks with Steam and holds the server to what you saw.** Each Workshop item shows Steam's own
  title, and is flagged when Steam calls it something else, it is not public, or it was published in the last two weeks;
  an item Steam does not list as a RimWorld mod blocks the start. If the server changes its mod list, its rules or its
  RimWorld Together version after you agreed, preparation stops instead of following. A load order that leaves out a
  mod Colony Nexus or RimWorld Together needs, such as Harmony, is refused. An optional mod is switched on only by the
  item you ticked.
- **RimWorld Together downloads are tighter.** A server asking for a pre-release, or a version older than the ten newest
  releases, needs your tick, and the release date is shown. Downloads start only from RimWorld Together's own release
  page, and Colony Nexus checks certificates itself, so another mod accepting any certificate cannot redirect them. A
  RimWorld Together copy Colony Nexus did not install is never removed when old copies are tidied.
- **Players can no longer pose as a Colony Nexus server.** On a RimWorld Together server without Colony Nexus, a
  player could take the server's reserved name and send Colony Nexus messages to everyone. Such a server echoes Colony
  Nexus's own traffic back, so Colony Nexus now recognises it, ignores the impostor and undoes what it set. A server
  reached over the internet can no longer point Colony Nexus at an address in your own network.
- **Chat and deliveries are harder to abuse.** A crafted gif or animated png can no longer freeze or exhaust memory.
  A Discord link code is shown only when you ask for one and is copied only when you click Copy. A game server can no
  longer write lines into Global chat. One delivery can no longer build more than a thousand stacks. A saved item may
  name only game types and a crate may not hold a pawn. Media answers nobody asked for are dropped, server-written
  reasons and names are shown as plain text, type names in server messages are never honoured whatever another mod
  sets, shared debug logs are scrubbed like reports, and video downloads no longer follow redirects.
- **Only Colony Nexus speaks as Colony Nexus.** A server, or a player on one, can no longer pass for Colony Nexus or
  its staff:
  - A staff badge names the server's own roles. A label such as "Global Mod", "C0lony Nexus" or "Official" falls back
    to the plain role, a role Colony Nexus does not know shows no badge, and the badge's tooltip says the server set it.
  - Only Colony Nexus Global's own directory can mark a server official, and the browser draws the star itself; a star
    or check mark in a server's name is removed. A directory typed into the mod settings is titled *Unofficial
    directory* and cannot mark any server official, and the release no longer stays on a playtest directory.
  - Notices and letters a server sends carry its name instead of "[Colony Nexus]", and so do its chat toasts. An
    unlisted server whose name poses as Colony Nexus is named by its address.
  - Signed in to Colony Nexus Global, a bug report's copy for the developers goes to Global directly. A server can no
    longer mark a copy it was never given as delivered, and a copy a server passes on says it was handed to that server.
  - Global chat keeps Colony Nexus's own colours and Discord marker, whatever the server sets, and a Discord message a
    server relayed into Global names that server. A server's Discord marker must be visible, and chat over the map
    names the channel first.
  - The Account page says the server accepted your Global sign-in, and the Federation window says its view is as the
    server passes it on.
  - Recent servers are remembered by a server's id only when the directory lists that id at the address you dialled.
  - After a Direct Connect, Colony Nexus asks the directory for that server's listing once, so a listed server is still
    named by its listing in messages and in Discord.
  - Leaderboard titles, season records, site names, reputation tiers and your Discord name are drawn as plain text.
- **Security fixes from the v1.3.1 release audit.**
  - **Delivered items:** an item delivered to you is checked again once it has loaded. A crafted saved item could hide
    a pawn, fire or building behind an XML namespace or a second item, and the check read the decoy. Such an item is
    refused before loading, and anything that loads as other than the item it was sold as is refused.
  - **Server mod lists:** a server's mod list is applied only as you agreed to it. The client now fingerprints the list
    itself. A list the server changes after you agreed opens the consent window again, and the review lists the mods
    switched on and off.
  - **JSON settings files:** a server's JSON settings file is read the way Newtonsoft reads it, so an escaped
    `$type` is refused too.
  - **Server names:** a server's name is drawn on one line everywhere. The connected message and the Federation
    window use the name the directory vouches for. A directory card's address is host and port only.
  - **Directory stream:** the directory's live stream checks the certificate itself. Where the game cannot, the list
    refreshes by polling instead.
  - **Debug info:** the shipped assemblies no longer carry where they were built.
  - **Licence text:** the Noto Emoji licence text ships beside the icons that use it.
- **RimWorld Together 26.10.20.1, RC15.** RimWorld Together's first full 1.7 release changed the method Colony Nexus
  hooks to start its session on joining, so with RC14 a 1.7 join never started Colony Nexus at all. The hook now fits
  both releases, and the self-test names any hook that no longer fits the RimWorld Together that is loaded instead of
  skipping it quietly. RimWorld Together 26.10.20.1 is needed on 1.7; 1.6 is unchanged.
- **See-through chat windows, RC15.** Mod settings → Chat window opacity (and the % button on a popped-out chat) now
  sets how much of the map shows through the Comms and chat windows, their panels included, from solid down to 30%.
- **Chat over the map stays out of the way, RC15.** It no longer covers the mouse-over readout at the bottom left, it
  moves up above an open menu such as Architect, a message pasted with line breaks shows as one line, and a line it is
  already showing no longer also pops up as a notification (mentions of you still do).
- **Lighter Colony Nexus tab, RC15.** The tab rebuilt its feature buttons, and the status panel re-wrapped its labels,
  on every GUI event, several times a frame; both are now kept and refreshed at most every half second.
- **Global quests take perishable goods, RC15.** Pork, meals and anything else kept as a full copy in the Treasury did
  not count toward a global quest, so "Supply Run: pork" could never be delivered. They count now; one delivery is paid
  from either those copies or ordinary stock, so with both the box offers the larger and a second delivery the rest.
- **The right RimWorld Together version is detected on 1.7, RC15.** Switching RimWorld Together read a copy's version
  from its 1.6 folder even on 1.7, so the Workshop copy, which RimWorld 1.7 loads as 26.10.20.1, was taken for
  26.10.15.1, and Prepare and join could fetch a version already installed. It now reads the copy's LoadFolders.xml,
  as RimWorld does.
- **Mods renamed in their 1.7 update still match, RC15.** Several Workshop mods changed their names in the 1.7 update,
  and the server browser listed such a mod as both missing and not allowed. Prepare and join then stopped, calling it
  not installed, or switched it off if the server listed it as optional. The server already accepts it by its Workshop
  id; the browser and the mod list now match it the same way, as long as that id names exactly one listed mod.
- **A deposit made just before a disconnect no longer stays pending for ever, RC15.** After a reconnect, RimWorld
  Together loads the server's last save, which may predate the deposit; the server returns such a deposit only once it
  is two minutes old, and the reconnect usually came sooner. When the Treasury shows a pending deposit this game
  neither saved nor made, Colony Nexus asks the server again once that time has passed.
- **Withdrawals no longer punch through roofs, RC15.** Drop pods aimed at a roofed stockpile crashed through the roof
  and damaged the room. They now land on an open cell of that stockpile, or beside it when it is fully roofed, and
  the message says so. Site workers returning by pod never punch a roof either.
- **Saves made while online always reach the server, RC15.** RimWorld Together uploads its own file after every
  save, but a permadeath save made any other way (Save and quit) was written under the colony's own name, so after a
  reconnect the upload failed with FileNotFound or sent an older copy. While connected, every save is now written to
  the file RimWorld Together uploads.
- **Want Board prices can be fractional, RC15.** Price per unit takes 0.55 like a Marketplace listing, on a server
  that supports it; the escrow shown is the total rounded up to whole silver, and the board shows the exact price.
- **No more "has no usable PawnGroupMakers" errors on joining.** RimWorld Together's own factions own work sites on the
  world map but have no workers for them, so every join logged an error per site, and every bug report carried it.
  Such a site now gets no workers without the error, as it already did with it; any other faction is unchanged.
- **Your share of the guild vault counts toward raid scaling again.** It was read from a copy of the vault the
  server never sends, so it always counted as zero, and silver kept in a guild vault was invisible to raids. It now
  comes from the guild snapshot every member receives, split by contribution as before.
- **Two mails that differ only in their goods are two mails.** Sending the same recipient, text and silver with
  different items within 20 seconds dropped the second one as a repeat.
- **Lists no longer leave the bottom row blank while scrolling.** The partly visible last row of every scrolled list
  (Marketplace, Auctions, Want Board, Quest Board, Guild Hall, Treasury) was sometimes not drawn.
- **Smaller fixes, rel-279.** The Account page retries an unreachable Colony Nexus Global every five seconds instead
  of every frame. A config file changed just after enforcement re-applied it is checked again rather than missed. A
  second RimWorld Together download request no longer marks the one already running as failed. A translation with a
  stray brace no longer breaks the main-menu card. Server text in the Connected Treasury window is shown as text.

- **Discord shows what you are actually doing, RC14.** A server's world event (a tax holiday, a market boom) no longer
  replaces your status with "Taking part in a server event" all session: it is a background line now, and it names the
  event. Cryptosleep sickness, morning sickness and toxic buildup no longer count as a disease outbreak, a single animal
  taking revenge is no longer "a manhunter pack" followed by "Recovering after a raid", and the game's minor low-food
  alert no longer outranks everything you do.
- **Find Steam friends works, RC14.** It always said Steam had not given a friends list: the call was looked up with the
  wrong parameter type and never found.
- **Fewer stutters, RC14.** An open chat asked the server for its whole history every 12 seconds and re-laid out every
  line even when nothing had changed; now an unchanged answer costs nothing. Opening the Colony Nexus tab re-requested
  all fifteen feature snapshots each time; now at most once a minute. The loading screen no longer re-measures the
  server name letter by letter every frame. With logging on, any Colony Nexus message or window that holds the game for
  longer than a frame is named in the log.
- **Save now, RC14.** A deposit waits for your next save before it is spendable, and RimWorld Together only offers
  "Save and quit". The Treasury's pending line now has a Save now button that runs RimWorld Together's own save,
  without quitting.
- **New chat over the map, RC14.** New server, guild and Global Chat lines appear at the bottom left for a few seconds
  while no chat window is open, and never block a click (Mod settings → Show new chat over the map).
- **Chat text no longer grows and gets cut off, RC13.** The chat log took its text size from whatever the game drew
  before it, so with some screens open its lines came out larger than the space measured for them and were clipped.
  The log now always measures and draws in one size.
- **Leaving a server that enforced its settings no longer gets stuck, RC12.** A backup made by an older build carries
  no baseline record, and restoring it failed every minute with "the personal config backup cannot be trusted",
  keeping the settings lock on. Restore now adopts such a backup the way applying always did; with no backup at all,
  the settings in place stay yours and the lock is released.
- **Colony Nexus everywhere you can see it, RC11.** The mod now unzips to `Mods\ColonyNexus` (it was `Mods\KMHPatch`),
  keeps its account, logs and reports in a `Colony Nexus` folder beside RimWorld's saves (it was `KMH-Patch`), and its
  log lines start with `[Colony Nexus]`. Nothing is lost on the way: the first start copies your settings over from the
  old folder name and moves the old data folder, account included. An old `KMHPatch` folder left in Mods beside the
  new one is now shown on the main menu with what to delete, since RimWorld would otherwise load either copy. The
  package id, the save-game names and the assembly names are unchanged, so saves, servers and extensions keep working.
- **The main menu card's status line fits, RC10.** It said "1 servers" and ran off the card once a server was
  joinable ("1 you ca..."). Counts now read "1 server" / "1 player" in English, German and Spanish, and when the line
  is too long the card drops the words "Global online" (the dot's colour still says it) before anything is cut; the
  full line shows on hover.
- **Discord status follows your game, RC9.** Raids (raiders, mechanoids, insectoids, manhunters, entities, sieges,
  drop pods), fires, medical emergencies, disease, food shortages, toxic fallout and the other disasters, caravans,
  visitors, ceremonies, research milestones and the season now show in Discord, read from the game's own threat,
  condition and map state, never from message text. A raid appears within seconds and ends in "Recovering after a
  raid"; everyday play rotates through a few accurate lines every ten minutes; nothing is sent unless it changed, and
  never faster than you allow. DLC and modded conditions that the game announces as bad are recognised by their own
  name. New settings choose simple or detailed wording, whether multiplayer, public server names, colony size and the
  play clock are shown, and how often Discord is updated; all of them start on, as does the Colony Nexus screen line.
  Private server names (unless you name them), quest details, pawn and item names stay out. Extensions can add their
  own activities through `KMH.Sdk.Client.Apis.PresenceProviders`.
- **Deliveries, RC7:** a delivery held while no colony was loaded now belongs to the session that replayed it, so it
  can no longer land in another server's game while its own server replays it again; a delivery that names a pawn,
  a building or a corpse is refused and stays owed instead of being spawned; a held delivery that fails part-way is
  not placed a second time.
- **Text from the server or other players draws as text.** Notices and "while you were away" letters, an item name
  this game has no def for, site worker names and video titles no longer render colour, size or bold markup, and
  long ones are capped. Unknown item names are no longer cached for the session.
- **A saved item may only name the game's own saveable types.** RimWorld builds whatever type a nested `Class`
  names before it checks it, so a stored item naming anything else is refused before it loads.
- **Chat channels keep their newest 500 lines**, and a line arriving in order is no longer sorted into place, so a
  long session's chat stays light.
- **The feature list from the API handshake is applied on the game thread**, like the chat hello's.
- **Unknown message kinds from a server are logged at most 64 times.**
- **Playtest packages come pointed at the playtest directory.** A package built for a playtest
  (`package-mod.ps1 -Directory preview`, named `...-playtest.zip`) browses the staging directory with the address left
  blank, and its settings say so; any other build uses the official directory as before.
- **Marketplace, Treasury, Auctions, Want Board, World and the mod settings are translated.** About 390 headings,
  buttons, columns, hints and messages in those windows (and the shared Close button and live badge) now follow your
  game's language in German and Spanish; plurals and word order are the translator's, not glued English pieces.
- **The Marketplace window allocates far less while open.** Each row's text is built once per update, language and
  second instead of on every frame, the column layout once per width, and the empty-table message only when the
  table is empty. Formatted labels no longer allocate while checking their placeholders.
- **Extensions can ask whether this game can hold an item.** `IItemLabelResolver.MissingContent(defName,
  stuffDefName)` answers as Buy, Bid and Withdraw do: null, or the DLC or mod content that is missing.
- **A withdrawal can no longer arrive twice.** A delivery with one stack this game cannot load (an item from a DLC or
  mod that is not active) used to drop the other stacks and stay unconfirmed, so every rejoin dropped them again. Every
  stack is now rebuilt before any is placed: if one cannot load, nothing lands, the delivery stays owed, and you are
  told once what is missing; it arrives when you join with that content active. A material or crated building this
  game does not have is refused rather than swapped for steel or delivered as an empty crate.
- **Buy, Bid and Withdraw say why when your game cannot hold the item.** An item, material or crated building from a
  DLC or mod you do not have is greyed out with the reason, instead of taking your silver for something that cannot
  arrive.
- **Prices in any language.** "1,5" and "1.5" both mean one and a half; "1,000" and "1.000" are refused as ambiguous
  instead of listing at 1 silver. The defend-quest duration reads the same way. The Marketplace total now rounds up,
  as the server charges.
- **Global Chat from the main menu.** A Global Chat button beside Browse servers opens Communications on Global Chat
  without joining a server; off a server it shows only what works there (Global Chat and messages across servers).
  A moderator's deletion now removes the line, blocking someone everywhere removes their lines already on screen, and
  Global lines show which server they came from, mark relayed names, and never carry this server's staff badges.
- **Click a name for the player menu** on the Marketplace, Auctions, Want Board, Quest Board and the Guild Hall roster.
  The menu gains Decline for an incoming friend request and Copy player ID.
- **Colonist records follow the colonist, not the nickname** (the server keys them on the pawn's id), and the
  Standings info card opens the right colonist when two share a nickname.
- **Lighter windows.** An open chat window allocated 174 MB/s at high frame rates, now 41 MB/s; chat images and
  animation frames drop their CPU copy once uploaded.
- **One Colony Nexus look.** The mark from the Colony Nexus website (a world, its ring and a moon) and its colours are
  now used everywhere: the loading screen, the main menu, the Colony Nexus tab, the leaderboard images, the mod icon
  and the Workshop picture, which still said "KMH PATCH". The mark is drawn at the size it is shown, so it stays sharp
  at every resolution and UI scale.
- **A full-screen Colony Nexus loading screen.** Joining or leaving a Colony Nexus server now shows a dark sky, the
  mark with its moon on an orbit, the server's name, a progress bar that fills only by what RimWorld measures (and
  travels otherwise) and a rotating Colony Nexus tip. On 1.7 the old panel sat above RimWorld's own tip and mod list;
  the new screen replaces the whole loading screen. Reduce motion stops the moon, stars and glow but keeps the bar
  moving; Animated visuals off stills everything. "Preview the loading screen" in Advanced & tools can show it full
  screen.
- **The main menu has one Colony Nexus card**, beside RimWorld's own menu: Browse servers, Account, About and Report,
  with your account status and the build. The old stack in the top-right corner covered the RimWorld title and the
  Ludeon logo on 1.7.
- **The Colony Nexus tab** shows the mark and name at the top and its features two to a row, so everything fits on a
  1280x720 screen without scrolling. The connection line names the server; its address and protocol are in the
  tooltip.
- **"KMH" became "Colony Nexus" everywhere you read it**: in English, German and Spanish, in every window, message and
  letter. The mod keeps its name, "KMH: Colony Nexus", because RimWorld Together admits players by mod name. A
  delivery stockpile can now be named with "Nexus" ("Nexus Depot"); names with "KMH" keep working.
- **The server browser puts servers you can join first.** A server for another RimWorld version no longer sits on
  top, even when it is the official one, and a server that only needs a RimWorld Together switch (which Prepare &
  Join does for you) sorts above ones in maintenance or full.
- **Clearer windows.** Sites is called Sites, its help text no longer cuts off mid-word, and each site is named by
  its type and output ("Farmland · potatoes"). Global quests show item names ("horse meat", not "Meat_Horse"). The
  Guild Hall explains what a guild gives you when you are not in one. Prepare & Join's summary fits its text instead
  of leaving half the window empty. The chat sidebar no longer cuts off player rows. A vault only mentions
  permissions when your guild rank limits them, instead of "Permissions: Deposit Withdraw" on every personal vault.
- **German and Spanish read properly.** About 85 German strings were written with "ae", "oe", "ue" and "ss" ("laeuft",
  "fuer", "ausser") and about 90 Spanish ones without accents or "ñ" ("version", "codigo", "contrasena"); four Spanish
  questions lacked their opening "¿". All now use the real letters.
- **The main menu card leaves RimWorld Together's own ▶ button beside the menu visible** (it covered most of it), and
  the config recovery warning never covers the card: at a large UI scale it moves below the card.
- **Chat:** the Global Chat entry reads "Global Chat (sign in)" instead of being cut off mid-word, says in a tooltip
  that it reaches every Colony Nexus server, and its status words are translated.
- What's New and the welcome window say "Colony Nexus" in their headings; the mod list keeps "KMH: Colony Nexus".
- **A light crosses the Colony Nexus name on the loading screen**, every few seconds, and the planet's glow warms as a
  load advances. On RimWorld 1.7 it can be drawn by the game's own graphics engine (Advanced & tools, "RimWorld 1.7
  shader effects", off by default while it is new); otherwise it is drawn the plain way, and it looks the same.
- **Every Colony Nexus window carries the mark beside its title**, and progress bars and "waiting for the server"
  states look the same everywhere (Roadworks, Federation, Prepare & Join, World, leaderboards, server settings).
- **Sites lists your own sites first**, then the ones you work at, then everything else.
- **Fixed:** every 1.7 start logged a warning about a skipped patch class, and RimWorld Together's Discord status was
  suppressed by two copies of the same patch.

### rel-278 (in progress)

- **Multiplayer works on RimWorld 1.7.** RimWorld Together could not send its first packet on 1.7's new engine: the
  serializer it bundles asks for a kind of generated code the engine does not allow. KMH changes that one request,
  in memory, before RimWorld Together connects. Nothing of RimWorld Together is modified on disk or shipped with KMH.
- **Leaderboards in Standings.** 22 boards for the server you are on: economy, trade, contracts, sites, Frontier,
  reputation, colony wealth, guilds and more. Each says whether the server measured it itself or took your colony's
  word for it. A podium for the top three, search, your own row highlighted, and a click opens a player's profile.
  A server older than rel-278 says it does not send leaderboards yet, instead of an empty list. When the server's
  standings are on its public page, "Open on the website" opens that board, and only on the directory address set
  in KMH's settings: a server can name its page, but it can never send you to another site.
- **The detailed record tables rank the way the boards do.** Equal values share a place, a row with nothing to show
  gets a dash instead of a place, and rows with equal values keep their order between refreshes. Three tabs ranked
  by formulas nobody could see are gone:
  - Player Standings "Overall": wealth/1000 + kills×50 + contracts×100 + reputation×10 + hours;
  - Colonist Records "Top Colonists": a kill counted as twenty skill points;
  - Battle Records "Survival": fewest colonists lost, which a player who never reported a colony led.
  Guild Standings "Overall" is now "Total Wealth", which is what it always summed.
- **The Colony Nexus loading panel.** Prepare & Join shows the step it is on and how many steps are done, the
  server browser shows it while the list is first fetched, and joining or leaving a KMH server replaces RimWorld's
  loading window with it. The bar is filled only by progress something actually measured; otherwise it sweeps.
  Advanced & tools has switches for animation, reduced motion and the loading screen itself. It needs no shaders, so
  it draws the same on 1.6 and 1.7.
- **Treasury deposits survive a reload correctly.** Each colony's save now carries its own deposit ledger id, sent
  with every confirmation. If the game is closed or crashes and the next join loads an older save, the server undoes
  only that colony's deposits made after it, so the goods are back on the map and not also in the vault. Deposits
  from your other colonies are no longer affected. You are told when this happens. Older saves get an id the next time
  they are saved.
- **Other players' settlements appear on the world map again.** RimWorld Together draws each player's settlement
  with the icon they picked, and does not check the number. A settlement whose owner never picked one (the picker
  was closed, or the settlement is older than icons) failed with an error and never appeared for anyone else. KMH
  now draws those with RimWorld Together's first icon, which is what RimWorld Together already does for your own.
  Nothing of RimWorld Together is changed on disk.
- **Prepare & Join never deletes a RimWorld Together copy it did not install.** RimWorld Together 26.10.4.1 can
  update itself: it replaces the loaded copy with a fresh download that carries no KMH marker. A later Prepare & Join
  set that copy aside like any other, and its tidy-up could delete it. Copies KMH did not install are now always
  kept, and the review no longer calls your own copy "the copy KMH put in your Mods folder".

### RimWorld 1.7 groundwork (development copy, not in rel-277)

- **A server on another RimWorld version says so and cannot be joined.** On Steam's 1.7 test branch, every 1.6
  server showed "Ready to join"; the join then hung for a minute. The card now reads "Needs RimWorld 1.6" and the
  details name both versions and where Steam chooses the branch. There is no Join and no Prepare for it, and nothing
  on disk changes. A session that reaches such a server anyway keeps KMH off and says why, before Config Enforcement
  can touch your mod list.
- **KMH runs on RimWorld 1.7's new engine** with the same files as on 1.6. A handful of Unity details 1.7 does not
  have (reverb zones, a video seek flag, the GPU vendor name) no longer break the feature that used them, and your
  saved Global sign-in keeps its owner-only file permissions on the new runtime.
- **Prepare and join remembers which RimWorld it was started on**, so switching Steam branches between two launches
  cannot resume a preparation made for the other version. RimWorld Together copies are read the way the game loads
  them on a version they have no folder for yet.
- **Without RimWorld Together, KMH stays quietly off** instead of logging errors about its tab and world markers.

### rel-277 (in progress)

- **Withdrawn and bought goods are delivered once, not again on every join.** The record that tells the server
  "this delivery reached my colony" was never created in a real game: the list that adds KMH's game components to a
  loaded game had left it out, and so had the one that lands goods held for a colony. No delivery was ever
  acknowledged, so the server re-sent each one on every join and the colony received it again. Found by playing:
  the same 99 silver arrived three times. KMH's game components are now found from KMH itself, not listed by hand.
  On a game from an older build, a delivery still owed lands one last time and is then acknowledged.

- **Prepare and join.** One button in the Server Browser takes you from "this server needs changes" to playing on it:
  - first it shows exactly what it will do: Workshop mods to subscribe (names and Workshop numbers), optional mods as
    tick boxes, mods switched on or off, the load order, the RimWorld Together version to install, and the restart;
  - then it subscribes and downloads (with byte progress, a stall retry and a clear message for an item Steam will
    not take), asks RimWorld to find the new mods, installs RimWorld Together from its official release page,
    writes your mod list, and restarts RimWorld;
  - after the restart it carries on by itself, checks that what loaded is what it set up, asks RimWorld Together
    for your account if you have none, and joins after a 5-second countdown you can stop;
  - your own mod list is kept the first time KMH changes it, through any number of servers, and **Restore my mod
    list** in the browser puts it back. The browser and Config Enforcement now share that one record.
- **Join asks for your RimWorld Together account first**, as RimWorld Together's own Join does, instead of
  connecting with an empty name.
- **After installing a RimWorld Together version, your mod list points at that copy.** A list naming the Workshop
  copy kept loading the old version after the restart.
- **Installing another RimWorld Together version works when Steam's library is on another drive.** The package was
  unpacked next to your save data and then moved into RimWorld's Mods folder, which Windows refuses across drives:
  every install failed with "Move will not work across volumes". It is now unpacked beside the Mods folder.
- **Switching RimWorld Together versions back and forth downloads nothing twice.** A server on your Workshop
  version sets KMH's copy aside so the Workshop copy loads again, and a version KMH installed before for another
  server is put back from where it was set aside. Only a version that is not on the PC yet is downloaded. The
  review says which of these it will do.
- **Restore my mod list also puts back your RimWorld Together copy.** A copy KMH placed in Mods outranks the
  Workshop one in every mod list, your own restored one included; restoring now sets it aside (and gives back a
  copy of your own that KMH had set aside).
- **A preparation that had failed comes back failed**, with its reason and Retry or Cancel, instead of starting again
  by itself the next time RimWorld opens. A RimWorld Together version that still has not loaded after its restart
  is reported, not restarted for again. The finished Workshop step counts the mods it downloaded.
- **A KMH letter that arrives before you have a colony no longer fails.** A guild invite received on RimWorld
  Together's new-colony pages threw and was only logged; it now shows as an on-screen message.
- **Server Browser layout:** the details pane is measured, so the Compatibility section no longer disappears on long
  details; names, verdicts, links and values are cut with "…" and shown whole in a tooltip; the open tab is
  highlighted; Sort, Tags and Clear are translated; the window keeps a usable minimum size and opens larger on big
  screens; one primary button replaces the row of step buttons that dropped the RimWorld Together switch.
- **Wording:** a Guild Hall member row reads "3 quests" (completed, in its tooltip) instead of "3q", and its name and
  figures no longer overlap. The Account & links window, when you are signed out, points you at its own Steam section
  instead of telling you to go to Account & links.
- **Servers past the first 50 are reachable:** "Load more servers", and Favourites, Recent and Friends read their
  servers by id, so a click on one always opens its details.
- **An expansion you own but switched off is a mod-list fix**, not "buy it on Steam"; mods installed but off are
  listed as such, and a list that only needs reordering says so.
- **A different RimWorld version is named before you join**, compared only as far as the server reports it.
- **A failed RimWorld Together release lookup is tried again** after two minutes instead of saying "no such
  release" for the rest of the session; cancelling a download is not reported as a failure.
- **Post Listing and Post Quest no longer say "0 = never expires"**: the server applies its longest allowed time.
- **Mail with only silver or items asks for a subject or a message**, which the server always required.
- **Boot no longer logs "skipped patch class FederationCache"** (an ordinary method was named like a Harmony hook).

- **The Guild Hall shows what is in the guild vault, and you can take it out.** Under the silver line, the vault's
  goods are listed (federation rewards, guild site output), and ranks allowed to withdraw get **Withdraw items**,
  which picks the item and quantity. The goods are delivered to your colony; the daily cap for your rank applies.
- **"Why am I not verified?" has a real answer.** If you joined through a local, private or tunnel address, the
  Federation window and Account & links (new "This server" line) say so and name the server's public address when
  the browser knows it, instead of asking a signed-in player to sign in. A refused identity ticket is retried on a
  growing delay, and signing in mid-session confirms you without a reconnect.
- **The dashboard counts quests awaiting your review**, and quests waiting for review count as yours.
- **A guild site's own output is offered only the guild vault and site storage**, labelled "Guild vault"; the colony
  and marketplace choices the server always refused are gone.
- **Quest review buttons, and Federation refusal reasons sent as codes, are translated** (English, German, Spanish).

### rel-276 — in development (not released)

- **The Server Browser's Friends tab works.** It lists the servers your Colony Nexus friends are playing on, with
  their names on the card. Each friend's own privacy settings are respected, and private or unlisted servers never
  appear. If you are not signed in, or a friend is online somewhere they keep private, the tab says so.
- **Mod setup tells you what is actually left to do:**
  - **Subscribe** for missing mods;
  - **Download** for ones Steam has not fetched or holds an old copy of;
  - **Steam is downloading…** while it does;
  - **restart** only once everything is on disk.

  Optional mods are offered on their own button and are never subscribed or switched on unless you choose them.
- **Set my mod list is safer:**
  - a mod that only shares a name with the one the server means is never switched on;
  - your optional mods stay as you had them;
  - with both a Workshop and a local copy of a mod, the one you use is kept;
  - a failed apply can no longer restore an old backup over your current list.
- **Join stays available when a server's mod list cannot be fetched**, since RimWorld Together checks it at the
  door. A failed fetch is retried instead of being stuck.
- **Getting a server's RimWorld Together version shows its progress:**
  - downloading, with Cancel;
  - installed, restart RimWorld;
  - failed, with the reason and a retry.

  It also finds versions older than the newest 30 releases.
- **Quest posters can reject a submitted bounty**, not only approve it.
- **The Federation window says when activity was never counted** after a long outage.

- **Catching up after a reconnect no longer freezes the game.** KMH spends at most a few milliseconds per frame on
  messages from the server (12 ms when far behind), skips snapshots that a newer one has already replaced, and if it
  falls hopelessly behind it reconnects instead of locking up. *Advanced tools → Performance* shows how far behind the
  game thread is, how many outdated snapshots were skipped and any failures.
- **Getting another RimWorld Together version is safer.** A download that is too large, stalls or is redirected
  away from the official release host is stopped. The archive is checked for oversized, unexpected or out-of-place
  files before anything is installed. A release with no checksum to verify against is never called verified: you
  are told, and it installs only if you choose to continue.
- **A withdrawal you could not place yet arrives once.** While you have no colony map or selected caravan, a
  delivery the server re-sent on each reconnect used to be held once per reconnect, and then every copy landed.

### rel-274 — a handier, moderated Colony Nexus website

- Nothing in the game changes. The *Colony Nexus page* link in the Server Browser opens the website, which gains the
  following once Colony Nexus Global is on rel-274:
  - copy a server's address, save favorites, see recently viewed servers, and pick a light or dark theme, all kept
    in your own browser;
  - a *What you need* box on each server page: versions, expansions, required mods, whitelist and how to apply;
  - filters for play style, who can join and new players, a *Good places to start* section, and readable page
    addresses;
  - report a listing that breaks the rules, with a moderator deciding what happens, and give a server you like a
    free boost into *Boosted by players* for a day, once your Colony Nexus account is a week old;
  - links shared in Discord and elsewhere preview properly.

### rel-273 — the website's finished look

- Nothing in the game changes. The *Colony Nexus page* link in the Server Browser now opens the finished website,
  with clearer server pages and a page for every situation (maintenance, offline, not found), once Colony Nexus
  Global is on rel-273.

### rel-272 — share a server's page

- **The Server Browser can give you a server's web page.** When Colony Nexus Global has its website, a selected
  server's details list its *Colony Nexus page* under Links, ready to copy and send to a friend. The page shows
  who is playing, what mods the server asks for and how to join.
- **Oversized chat images are refused before loading.** A PNG or JPEG whose header declares more than 8192 pixels
  on a side or about 16.7 megapixels is not loaded. The chat says it is too large to show, instead of the game
  trying to hold a picture that size in memory. Screenshots, 4K images and 12-megapixel photos still show.

### rel-271 — player reports get their own table on Colony Nexus Global

- Nothing in the game changes. Colony Nexus Global stores player reports in their own `player_reports` table, so
  its database step no longer collides with the older moderation table called `reports`, which is left untouched. A
  report sent from the main menu, or forwarded by a server, is stored once Global runs rel-271.

### rel-270 — reports, smoother online play, easier reading

- **Report a bug or an idea from inside the game.** The button is in the Colony Nexus tab, under Advanced & tools
  and on the main menu, and every KMH window that fails to draw now offers "Report this problem".
  - The window takes a screenshot of the game as it was before it opened, and you choose whether to attach it.
  - You can also attach diagnostics: the end of the KMH log, the game's recent errors and warnings from every mod,
    your mod list, frame rate and game speed, system and graphics details, and your KMH settings.
  - "See what's sent" shows the diagnostics line by line before anything leaves your computer. Passwords, account
    tokens, chat and save files are never included, and your Windows user folder is removed from every path.
  - The window says who will receive the report before you send it, and afterwards who actually did. On a server,
    the owner gets it, and the Colony Nexus team as well if the owner has turned that on. From the main menu it
    goes to the team through your Colony Nexus Global account. If nobody can receive it, it is only saved.
  - Every report is also saved under `KMH-Patch/Reports` (the newest 20). "Send again" after a lost answer keeps
    the same report id, so it is never filed twice.
- **A large update from the server no longer drops your connection.** A big compressed message, such as the
  marketplace catalog, unpacked past 64 KB, so the client refused it and reconnected every time it was pushed.
  The client now unpacks up to 8 MB and tells the server so. An older server that never says what it can unpack
  is sent anything larger as fragments.
- **Sending never holds up a frame.** Messages are compressed, split and written on their own thread, in order,
  instead of inside the click. A large send on a slow uplink used to freeze the game until it finished.
- **Large transfers are put together off the game's thread**, so a 1 MB snapshot no longer costs a frame to decode
  and check. Incoming work is spread over a few frames, about 4 ms of each, instead of landing in one.
- **The client follows the server's frame size.** An owner who set a smaller frame than 64 KB had every larger
  client frame refused.
- Every message read used to leave a 40-second timer running until it expired - thousands at once on a busy link.
  The timer now ends with the read.
- **Performance panel** under Advanced & tools: frame rate and the slowest 1% of frames, game speed against the
  speed you picked, what each piece of KMH's per-frame work costs, and network totals. It can also show one line
  in the corner of the map.
- **Easier to read.** Secondary text that used the dark grey (#808080) is now a lighter, slightly cool grey
  (#A9AFB9), in every window and in all three languages.
- What's New lists both. Every new piece of text is in English, German and Spanish.

### rel-269

- No client changes; the build number follows the rel-269 Server Addon.

### rel-268

- No client changes; the build number follows the rel-268 Server Addon.

### rel-267 — What's New for this release

- **The What's New window describes v1.3.1.** It had gone on showing the v1.3.0 notes. It now covers the server
  browser, mod compatibility before joining, the Colony Nexus Global account, Guild Federation, settings applied
  while you join, colonist workers, Discord linking, and why both sides update together - in English, German and
  Spanish.

### rel-266 — Federation reasons in your language

- **The Federation window explains itself in English, German and Spanish.** Why a guild left, why a federation
  ended, and why a link has to be proved again are shown as sentences, never as internal codes such as
  `identity_proven` - including the history written by servers on earlier builds.
- **History entries read naturally**: who took a guild out or disbanded the federation, which profile fields
  changed, and which server an invite was for.
- A weekly objective's supply cache is paid once per guild, even after the guild leaves and rejoins.

### rel-265 — plain text in the Guild Hall

- **A guild's message of the day is shown as plain text.** An officer's message is drawn for every member, and
  markup in it could restyle the whole Guild Hall.
- **Player names are drawn as plain text everywhere they appear**: chat, mail, the marketplace, auctions, quests,
  sites, profiles and the Guild Hall, including its ownership-transfer menu.
- Changes to your federation reach your server within about a minute, also on a server whose guilds are in more
  than one federation, where a change could take ten.
- If your server's Colony Nexus Global registration is revoked, the Federation window says so, rather than showing
  what it last knew as if nothing had happened.

### rel-264 — Guild Federation

- **Guild Federation.** Guilds on different Colony Nexus servers can join one federation, with its own name, tag,
  colour, leading guild and history. The **Federation** window has Overview, Members, Objectives, History and
  Manage tabs, and it is on by default on every server.
- **Joining takes an invite made for your server.** Each invite works once, for that one server, expires after
  30 minutes, and can be withdrawn until it is used. Nobody types a raw federation id.
- **Renown and levels come from verified play** on member servers: approved quests, Living World quests, site
  production, road sections, outposts and daily activity, each capped per day. Weekly shared objectives pay a
  small supply cache into your own guild's vault on your own server. Base-game items come first; an expansion item
  only when your server requires that expansion.
- **Small local bonuses by level**: +1% site output at level 3; +2% site output and road building at level 6;
  +3% of each at level 9.
- **Leaving is not disbanding.** A member guild can leave at any time. The leader hands leadership on first.
  Disbanding needs the tag typed back, keeps the whole history, and can never be undone.
- **Founding, joining and managing need your Steam-linked Colony Nexus account**, confirmed on that server. A
  guildmate who has not linked stays a normal member; their play does not count until they link.
- **Nothing moves between servers.** The Connected Treasury is off in this release: no silver, items or resources
  cross from one server to another, and its windows do not appear.
- The tag of your federation shows beside your guild's name in the Guild Hall.
- Getting a server's RimWorld Together version from the browser now installs it into RimWorld's own `Mods`
  folder. It was written to a folder RimWorld never loads, so the switch never took effect.

### Unreleased — the browser tells you what a server is

- **Server listings say something now.** A server's own description appears in the browser under **About this
  server**, above the numbers — until now every listing looked alike, because the only things shown were a name,
  a region, some tags and a player count.
- **Discord and website links**, where the owner published them, with a **Copy** button. They are never opened for
  you: the address is typed by another server's owner, so it goes to your clipboard and you decide.
- Saved listings are made inert when they load, the same as freshly fetched ones.

### rel-221 — Global Chat and one identity across servers

- **Global Chat.** A channel shared by every connected KMH server, carried by KMH Global rather than by the server
  you happen to be on, so it survives that server restarting. Your name in it is your KMH account, not a per-server
  handle.
- **You can block someone once and stay blocked**, across servers and across Global Chat, rather than repeating it
  everywhere.
- **KMH traffic is compressed** where the server supports it. The saving is largest exactly where it hurt most: a
  site catalog that used to arrive as nine fragments now arrives in one.
- **A server name or tag from the directory can no longer become markup.** RimWorld's labels parse rich text, and
  the browser was drawing text written by other server owners raw - a name could restyle the row around it.
- The browser understands a server in maintenance and stops offering a Join that would be refused.
- **Your account token is handled more carefully.** A custom directory address on another machine can no longer be
  reached over plain `http://` - that put the token on the wire in clear - and `account.json` is restricted to your
  own Windows account where the platform allows it. Loopback addresses still work for local development.
- **A link expires on two clocks now**: 30 days without being used, and 180 days from when it was made however
  often you play. An expired link is re-made from the Account panel and keeps the same KMH player id.
- **A server on the old protocol ends the session cleanly.** The client says so once, turns KMH off for that
  session and hydrates nothing, rather than half-loading state a v2 server cannot serve.

### rel-220 — Steam identity

- **Link your Steam account to KMH Global** from in game: the client shows a code, you approve it in a browser. That
  link is what gives you one identity, one username and one standing across every KMH server.

### rel-219

- No client changes; rel-219 is the server running natively on Linux.

### rel-218 - current-RWT server browser

- **Join actually joins.** The browser's Join button used RWT's own connect path (`Network.Ip`/`Port` then
  `ClientNetwork.StartFeature`), which is byte for byte what RWT's login dialog does. It is disabled, with the
  reason beside it, only when something real blocks the join.
- **No state sits at "pending".** Mod lists are compared against the server's published profile, and a card reads
  Ready, Ready with optional differences, Mods need changing (naming the mods), Mod list unavailable, No address
  published, Server full, RimWorld Together differs, KMH version differs, Not ready or Offline.
- A server on a different RWT release offers RWT's own version downloader where that build has one, and explains the
  mismatch where it does not.
- **Chat media volume is remembered.** It always saved; the transport bar read a static field that nothing had
  loaded, so every launch showed the shipped 60% whatever the settings file said. Muting and looping had the same
  fault. Settings are updated the moment the slider moves, and only the disk write is debounced.
- The main-menu entry is larger, carries the KMH mark, and says how many listed servers this client could join now.
- Favourites can be set from the browser, offline servers stay out of the default view but remain searchable and in
  Recent, and the Friends tab says what it needs instead of showing an empty list.

### rel-217 — v1.3.1 stabilization candidate

- **Wire protocol v3.** A v2 (1.3.0 or earlier) peer is now refused with both version numbers named, rather than
  connecting and quietly missing half the release. The client declares the mismatch instead of going silent, so an
  enforcing server ends its pre-world hold at once rather than after a timeout.
- Site workers are colonists, not accounts: one player can staff a site with several different colonists up to its
  capacity. Existing sites re-key themselves on load and keep their XP, cycles and destination.
- Recall names one colonist when several are assigned, instead of pulling the whole crew off the site.
- Minified buildings can be deposited: they travel as a full deep-serialized payload only, and are refused outright
  rather than rebuilt from def+count, which would hand back an empty crate. Item keys now name the building inside,
  so two minified things are no longer one picker row.
- Treasury opening fee: an explicit open buys a short access session. Refreshing, reconnecting and reopening inside
  that window are free, and the request is idempotent, so no retry can debit twice.

### rel-216 — manual acceptance candidate

- Config enforcement verifies the current profile before the server releases world/save progression.
- Config apply and restart state are transactional and durable; reconnecting with the same profile needs no second restart.
- Consent text describes reconnect persistence and the zero-or-one restart policy. Manual RimWorld acceptance remains pending.

### Added

- **KMH Servers browser.** A tile on the main menu opens a list of KMH servers with search, All / Official /
  Favourites / Recent / Friends tabs, and a detail panel showing players, status, region, languages and protocol.
  It draws from the last completed refresh and never blocks the menu: if KMH Global is unreachable the window
  still opens and says so, showing saved results.
- German and Spanish cover every new string.

## v1.3.0

RimWorld 1.6. Verified against the nine most recent RimWorld Together releases — 26.8.31.1, 26.8.16.1 (1),
26.8.16.1, 26.8.9.1, 26.7.25.1, 26.6.23.1, 26.6.9.1, 26.6.8.1 and 26.5.24.1 — and the mod loads the matching client
for whichever you are running, with nothing to configure. 26.5.24.1 is the oldest supported release: RWT moved a core
network type below that point, so 26.4.18.1 and older are a different generation and are not supported.

Protocol stays at v2, so this client still connects to a v1.2.x server; the new features simply stay hidden until the
server offers them. Nothing in your save needs converting.

### Added

- **Standings show what you actually did.** Sites and worker XP used to read zero for everyone. Worker XP is now the
  XP your own colonists earned, wherever they worked - a site owner no longer absorbs it. "Sites" is what you control
  right now, and a separate lifetime count tracks what you built.
- **Frontier records.** The outposts you hold, and the claims you have won. A claim is history: losing the location
  later never takes it off your record, and it carries into the season archive.

- **Frontier Works.** Pick an archetype when you build a site — Farmland, Quarry, Woodland, Ranch, Roadworks or Custom. The
  archetype decides which colonist skill the work uses; Custom keeps whatever the output implies. Sites you already
  own are unchanged.
- **Site buildings and storage.** Each site has a couple of building slots (more at higher tiers). Build production
  for more output, housing for more workers, or storage so the site holds what it makes until you collect it in bulk.
  Anything that will not fit in storage still arrives in your treasury. Demolishing refunds nothing.
- **Site condition.** A damaged site produces less, but never nothing — and you can repair it from the site's
  building window for a price based on how much damage there is to undo.
- **Roadworks.** Plan roads from a Roadworks site: pick a destination on the world map and KMH routes it, quotes the
  cost before you commit, and builds Trail, Road or Highway depending on your site's tier. Roads already on the map
  are not charged for again. Cancelling returns the silver for anything still unbuilt. KMH only ever writes roads it
  planned, remembers what it replaced, and puts that back on removal — roads from worldgen or another mod are left
  exactly as they were.
- **Frontier Operations.** Derelict and restored locations show up in your Sites window beside your own, with what
  they are, their condition, and how much of the claim window is left. When the server says you earned one, a Claim
  button takes it for yourself or for your guild. Your client also answers the server's placement questions in the
  background — it offers candidate tiles from your world map and decides nothing on its own.
- **Communications hub.** Server chat, guild chat and direct messages in one window alongside your mail, with unread
  counts on the KMH tab so a message is noticeable without watching the chat window.
- **Pop-out chat.** "Pop out" in the hub moves the channel you are reading into a small window you can leave open
  while you play — it does not pause the game and does not block clicks on the map. Drag and resize it wherever you
  like; it reopens where you left it. Switch channels from its own title button, or press "Hub" to go back to mail,
  blocking and Discord linking.
- **Player mail.** Write to another player and attach silver, items or full-condition gear. Attachments are held by
  the server, can be recalled while unread, and come back to you automatically if never claimed.
- **Notification controls.** Per-channel levels (silent / toast / toast + sound) for server, guild and DMs, a mute
  toggle in the hub, and matching settings in the mod options panel.
- **Blocking.** Block a player to stop seeing their messages in live chat and in history.
- **Pictures, GIFs and video in chat.** Images shared in chat — including from Discord — appear inline, GIFs animate
  (RimWorld cannot decode one, so KMH brings its own decoder), and video plays inside the game without downloading
  anything or writing to disk. Video has a full transport bar: play/pause, seek, volume, repeat, and fullscreen with
  Space, M, F and the arrow keys. Volume rides on RimWorld's own master volume, and your volume, mute and repeat
  choices are remembered. Nothing loads on its own — the placeholder names the host and your click is what fetches
  it, because loading tells that host your IP address. Server owners decide which hosts are allowed at all, and
  "Load chat images automatically" in the mod options turns the click into a standing answer.
- **Off-map wealth counts toward raids.** Value held in your treasury, guild vault, listings, auctions, wants, quest
  bounties and mail attachments now feeds RimWorld's threat scaling. The server decides whether this is on.
- **Client extension SDK.** A RimWorld mod can now react to KMH events — payouts received, world events, notifications
  and every feature cache — with a worked example in `Templates/ClientExtension-Earnings/`.
- **Decimal Marketplace pricing for extensions.** Marketplace extensions can now post normal decimal silver prices
  such as `100.23`, without working with internal scaled currency units.
- **Mark all read in Communications.** You can now clear every Communications unread notification at once
  instead of opening each conversation one at a time. It appears only when something is unread.

### Fixed

- **Animated pictures posted as links now animate.** A gif or animated WebP shared in Discord as a *link* arrived in
  chat as a flattened still, while the same picture posted as an *attachment* animated correctly. Discord attaches a
  link's preview by editing the message a moment later, and that second path skipped the conversion the first one
  did. Both paths now convert, so an animated link plays like any other.
- **A picture the host no longer has is asked for once.** One dead Discord link was fetched eleven times in seven
  seconds, because every failure was treated as worth retrying. A refusal that will not change — the host does not
  have it, will not serve it, or sent something the game cannot read — is now remembered, and the row says so instead
  of offering a retry that cannot work. Timeouts and rate limits still offer one.
- **A newly built road appears on the world map straight away.** Marking the road layer for redraw threw on worlds
  with more than one kind of map layer, so a road was written to the planet and then not drawn until something else
  happened to refresh the map — which is why a road often only showed up once you started building another one.
- **Unread notifications no longer clear before you have read anything.** Opening Communications, or letting it
  refresh, stopped clearing the unread alerts for channels and DMs you were not looking at. Unread counts now stay
  until you actually view that conversation, so the channel counts, the Communications button and the KMH tab badge
  all agree.
- **Frontier now progresses on its own.** The world director asks connected players where a new outpost could go, but
  that question only reached players who happened to have a KMH Frontier window open at that moment - so on a normal
  server nobody ever answered, every question timed out, and no outpost was ever established without an owner
  command. The question now reaches every connected player, whose client answers quietly in the background.
- **Marketplace pricing stays compatible for existing extensions.** The existing Marketplace posting API continues to
  read integer prices as normal silver, so an older integration does not silently change its listing prices on v1.3.0.
- **The Marketplace "Listed" column no longer truncates to "List…".** The table now sizes that column first and shares
  what is left between the others, so it stays readable at every window size.
- **Roadworks help text is no longer cut off.** The three lines explaining how roads get built now wrap instead of
  running past the edge of the window.
- **Roadworks no longer says "From Tile 72540 (tile 72540)".** An unnamed site is identified once, not twice.
- **"Pick destination on map" now says why it is unavailable.** If the caravan route planner is open it owns the world
  map, and the greyed button explains that instead of just looking broken.

- **Granted goods are never silently dropped.** If a payout cannot be placed (no colony or caravan available yet) it
  is held and delivered when one appears; a genuine failure raises a persistent letter instead of vanishing.
- **The KMH tab could look like it had collapsed to three buttons.** The status table sat above the actions and pushed
  them below the fold on a short window. Actions are drawn first, and the layout is now checked in the build.
- **The Discord link code now actually appears.** Asking for a code did nothing visible - the dialog was built on the
  network thread and never reached the screen, and the error was swallowed. It now shows and copies to your clipboard.
- Item stacks carrying saved condition are no longer mis-counted when filling a want.
- Remote text can no longer resize, recolour or bleed formatting into your panels.
- Direct-message channels are consistent regardless of who opens them or how names are capitalised.
- Switching servers no longer leaves the previous server's season records on screen.
- Item, player and guild pickers no longer rebuild their lists every frame; the deposit picker no longer rescans your
  whole colony twice per frame.
- Notifications and letters raised from network messages are marshalled to the main thread.
- A site showing "× 1.00" now explains that it is at its tier cap, including against older servers that do not report
  the cap.
- **A panel that failed to ask for its data now asks again.** If a request was refused during the moment of
  connecting — the window where the transport is still coming up — that one panel sat on "Loading…" for the rest of
  the session while everything else filled in. Only the four world-visible features were ever retried; now every
  feature is, on the same bounded schedule, and only the ones that actually failed.
- **The item catalog is no longer treated as sent when only part of it arrived.** One chunk of five counted as a
  successful upload, which left the server with half a modpack and no reason to ask for the rest. A push now counts
  only when every chunk lands, and a failed one is retried during the session instead of waiting for a reconnect.
- **Build Site no longer sticks on "Pricing…".** If the quote request was refused, the same item and amount would
  never ask again. A failed request now retries, and one that goes unanswered stops blocking a fresh attempt.
- **A message from a server you just left cannot land in the next one's screens.** Data arriving in the instant a
  session ends is now discarded instead of being applied after the switch.
- **A stale reply can no longer put spent silver back on screen.** Two answers about the same panel can cross in
  flight — one the panel asked for, one pushed because something changed — and whichever arrived last won. A sold
  listing reappeared, a spent balance came back, and the only way to find out was to reopen the window. Screens now
  ignore an answer older than the one they are already showing, per vault, so your personal and guild balances no
  longer overwrite each other either.
- **A click that has to travel twice only buys once.** When a request could not be confirmed, KMH gave up on it and
  told you to try again if nothing happened — so a purchase, listing, want, quest, mail attachment, site building,
  road, guild perk, withdrawal or global-quest delivery could be lost, or done twice if you did try again. Such a
  request now carries the identity of the action, is re-sent on the other transport, and the server applies it once.
  Repeating the same action within a few seconds is treated as waiting on the first one; a moment later it is a new
  action and goes through.
- **KMH world markers no longer cost frames on the planet view.** Every site and outpost marker rebuilt its texture
  path from scratch on each frame it was drawn, at both zoom levels, which the garbage collector then had to clean up
  — noticeable on a world with a lot of KMH locations. The paths are worked out once now.
- **Delivery receipts stop re-checking themselves forever.** Once a save had collected receipts, the client walked the
  whole stored list every frame for the rest of the session even when the server had already acknowledged all of
  them. It now stops as soon as nothing is outstanding, and picks up again on a save or a new server.
- **In-game video actually plays.** Every attempt failed. RimWorld's Unity refuses a plain-http address outright, and
  that is what the server's video relay serves — so the download never started, and the two dead attempts left the
  file locked so the third reported "failed to create file" instead of the real reason. KMH now pulls the video down
  itself rather than asking Unity to, and a cancelled or stalled download cleans up after itself.
- **You can tell a chat-only server from a broken one.** Both showed the same amber "RWT chat fallback". When a
  server advertises a KMH transport that cannot be reached, the tab now says so and the settings screen names the
  address and the reason, so it reads as something to fix rather than as normal operation.
- **The transport settings say what they actually do.** The port field is named as the fallback it is — the server's
  own advertised port has always won — and the host override now says it applies to every server you join. When a
  link is up, the screen shows the address KMH is really using rather than leaving you to infer it from the config.
- **A self-test no longer reports missing marker art that is not missing.** Its staged lookup failures tripped the
  real "world marker art missing" warning, sending owners after a file that was there all along.
- **A deposit that is refused for being too large now says so.** It reported "could not take the items", which reads
  as your stock having gone missing rather than as a limit you can work around by depositing less at a time.
- **Marker art can no longer render upside down.** KMH shipped three copies of every texture — a `.png`, a `.dds`
  and a `.dds.zstd` — and RimWorld's DDS loader reads rows bottom-up, so a `.dds` has to be stored vertically
  flipped to look right. Only one of the three was ever verified, so a wrong copy showed a marker the wrong way up
  while everything still reported healthy. KMH now ships PNG only: 114 texture files become 38, and the convention
  that could be got wrong no longer exists.
- **Zoomed-in markers sit the right way up.** RimWorld lays a world-map marker flat on the planet with the top of its
  picture pointing east, not at the top of your screen. Vanilla markers are symmetrical dots, so it never showed;
  KMH's are a castle, a shield, a factory, and they were lying on their side. KMH markers now work out which way up
  is, wherever they are on the planet and however the view is turned.
- **KMH sites are recognisable on the world map.** A site you or your guild can manage draws a green rim, another
  player's a blue one, and an outpost takes its state colour, because claimable and hostile are the ones you cannot
  afford to miss. It is the same border RimWorld itself draws around a settlement you own — drawn by the game's own
  shader, not imitated — so the artwork keeps its own colours, and your guild hall is marked as yours too, where it
  previously had no look of its own at all. A place nobody holds draws no
  rim, so the ones that matter carry weight. The border can be switched off, and the two ownership colours set, in the mod
  options, and a server owner can suggest colours for their players; outpost states always keep KMH's colours, so a
  warning can never be styled into something calm.
- **Roads you have built now appear when you join.** KMH remembers which road tiles it wrote so it can put back what
  it replaced. RWT rebuilds the planet when you connect, which wipes those roads, and KMH read that as "somebody else
  owns this now" — it dropped the record and, with it, the road, then marked the whole network as up to date so it
  never looked again. Every road you had built stayed invisible until something unrelated made the server re-send the
  list, which is why building a new one appeared to fix it. Losing a stale record no longer loses the road with it.
- **A KMH road that gets wiped off the map puts itself back.** KMH used to write its roads once and then trust a
  version number to tell it whether anything had changed. RimWorld Together re-sends the state of the planet after
  KMH has already written, which quietly erased those roads — and because the version number had not moved, KMH never
  looked again. It now checks the tiles it has written against what the map actually shows, a couple of seconds
  apart, and re-lays anything that has gone. A road being wiped no longer counts towards the limit that stops KMH
  fighting another mod over a tile, so this keeps working however many times it happens.
- **A road you paid for is now actually laid.** You are charged per segment that KMH has not already built — but the
  game's own ancient roads outrank every tier KMH can lay, and KMH refused to touch a tile that already had a better
  road. On a planet criss-crossed with ancient roads that meant paying for a route and watching almost none of it
  appear. A segment your project covers is now laid whatever is already there, and the road it replaced is remembered
  and put back if you remove yours. Roads KMH never planned are still left alone, and if something else keeps putting
  its own road back on a tile, KMH stops after a few tries rather than fighting it forever.
- **"Never share my logs" now means never.** The three-way log-sharing choice was written by the settings screen and
  read by nothing: picking Never left you being asked on every server exactly as if you had picked Ask. All three
  settings do what they say.
- **The Sites window stopped hammering the server.** It asked for a fresh site list every six seconds for as long
  as it was open, and a build-site price quote was requested sooner than its own delay allowed. Both counted their
  timers down from the drawing code, which RimWorld runs more than once a frame, so they fired at roughly twice the
  rate they read. They now tick once a frame like every other window.
- **The world map costs less to draw.** Every KMH marker was drawing its border as eight extra copies of its own icon
  each frame, on top of a sweep over every object on the planet on every repaint — all of it replaced by the one hook
  RimWorld already reads for its own icons. The marker reconcile, which runs twice a second for the whole session, no
  longer allocates fresh lists each pass, and the marker colours are worked out when they change rather than per
  marker per frame.
- **Your KMH log is readable again when a server has debug on.** Half of it was the client self-test listing all 409
  checks it passed, on every connect, and a "pong" line every fifteen seconds. Failures still name themselves, the
  pass count is still reported once, and the heartbeat is summarised hourly instead of per beat.
- **A failed connection no longer corrupts the log file.** Windows hands back a connection-timeout message with its
  raw tail still attached — a line break and a run of zero bytes — and KMH wrote that straight into the file, which
  made the log read as a binary and broke text tools on it. Every KMH log record is now a single line of text.

### Changed

- **A delivery is no longer built before KMH checks it has somewhere to go.** The two questions — "did this land?"
  and "is there anywhere to land it?" — were asked in that order, so the items were constructed first and the
  answer arrived second. During a load that construction failed noisily, and if your colony finished loading
  between the two questions the grant was classed as undeliverable, which is the one outcome that is never retried.
  KMH now asks first and builds only if the answer is yes.

- **Goods you received are now marked as received.** KMH told the server a delivery had landed at the moment it
  *arrived*, not the moment it was actually placed — and a re-sent delivery arrives while the world is still
  loading, when there is no save to record it against. So the goods dropped into your colony, but the receipt was
  never written and the server kept re-sending them. The receipt is now written where the goods actually land,
  including for a delivery that had to wait for your colony to load, and the server stops asking once it is told.

- **Goods owed to you no longer jam the session on every join.** A delivery the server still owes you is re-sent when
  you connect — but it arrived while the world was still loading, and the check for "is there anywhere to put this?"
  threw instead of answering "not yet". That error escaped, so the delivery was neither placed nor held nor
  acknowledged, and the server re-sent the same goods on every join for ever, logging an error each time. KMH now
  answers "not yet", holds the delivery, and drops it to you once your colony is loaded.

- **One fault no longer takes the rest of the frame with it.** KMH does several things each frame — the main-thread
  pump, the image queue, video, world markers, roads. They ran in one unguarded sequence, so anything that threw
  stopped everything after it and repeated the error every frame. Each step now runs on its own and a fault is
  reported once, leaving the rest of KMH working.
- **Loaded pictures no longer pile up for the whole session.** Bytes the server converted were kept indefinitely so
  an image scrolled past and back could be redrawn without asking again. A session of animations at several MB each
  made that a leak; KMH now keeps the most recently seen and lets the rest go, fetching them again if they return.

- **The chat log is laid out only when it changes.** Every frame the Communications window was open, KMH rebuilt and
  re-measured the formatted line for every message in the channel, whether or not anything had moved. Those
  measurements are now kept until the log, the channel or the window width actually changes. Picture heights are
  still worked out every frame, so a gif finishing its download still pushes the rows below it down exactly as before.
- **Your item catalog is sent once per server, not once per connection.** Site metadata — around 137 KB for a vanilla
  game, more with mods — was re-streamed over the chat channel on every reconnect. It now follows the same ten-minute
  rule the item labels already used: same server, same modpack, nothing re-sent. A server that reports it is holding
  no catalog still gets one immediately, which is the case the rule exists to protect.

- **Global quest deliveries come from your KMH treasury, not your stockpiles.** Deposit what the quest wants, then
  press Deliver — the server takes it from your vault. Nothing is removed from your colony or caravan by the Deliver
  button any more, and the amount offered is what your treasury holds. Anything the quest has no room for stays
  where it is. This is what lets the server actually verify a delivery.
- **Site output counts the XP your workers earned, not the skill your client reported.** A worker's pawn skill is
  still shown, and still decides nothing on its own; the site's production and road progress now come from XP the
  server awarded for work done. A site staffed by a freshly assigned expert will start slower than before and speed
  up as it works.

- **KMH command help shows complete commands.** `kmh help` now lists copy-pasteable invocations such as
  `kmh frontier status` instead of bare fragments that read like commands of their own, grouped by topic with a short
  line each. `kmh help <topic>` covers one area, and an unknown command says so rather than printing the whole menu.
- **`kmh frontier status` explains itself.** It now reports the director's state, budget, outpost and operation
  limits, when the next automatic action is due, how many players were last asked to place an outpost, and the
  reason automatic progress is blocked when it is. `kmh frontier status verbose` adds the raw timers.
- **Marketplace toolbar is split in two.** One row narrows what you are looking at — search, category, "Only mine",
  "My guild only" and sort — and one row holds the buttons that take you somewhere or act on the market. Category is a
  proper dropdown, and an empty table now names the filters that are hiding your listings.
- **Filter tick boxes read as on/off.** An unticked filter in Marketplace, Auctions, Quests and the Want board showed a
  cross, which looked like a warning; it is now an empty box, and the label is clickable too.
- Roadworks project cards lead with the road and where it starts, tier options quote their price per segment, and the
  reserved silver is shown once per project with the total above the list.
- The KMH tab refreshes everything when opened, and the tutorial covers communications and mail.
- "Offline mail" is now called "offline notifications" — Player Mail is a separate feature.
