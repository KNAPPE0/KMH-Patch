# Changelog

All notable changes to the KMH Patch client mod. Versions follow the KMH build number shared with the KMH Server Addon.

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
