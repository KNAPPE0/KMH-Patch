My KMH Accent - example client extension
=========================================

A minimal complete client-side extension as a stand-alone RimWorld
mod. Pops a green "nice - N active listings" toast whenever the
player gets a new marketplace listing. Use it as a starting point
for your own client-side extensions.

Source tree:
  About/About.xml          mod metadata (Colony Nexus dependency declared)
  MyAccent.csproj          build configuration (references SDK only)
  Source/MyAccentExtension.cs   the extension implementation
  README.txt               this file

To build:
  cd Templates/ClientExtension
  dotnet build -c Debug

Output lands at:
  1.6/Assemblies/MyAccent.dll

To install:
  1. Copy the entire folder (About/ + 1.6/) into RimWorld's Mods
     folder, or upload as a separate Steam Workshop item.
  2. Make sure KMH: Colony Nexus is also installed.
  3. In-game Mods menu: enable KMH: Colony Nexus first, then My KMH Accent.
  4. Restart RimWorld.

You should see in Player.log (or the Colony Nexus log file):
  [Colony Nexus] [ext:My KMH Accent] Registered. Will cheer on new listings.
  [Colony Nexus] Extensions: scanned N candidate assembly/ies, loaded 1.
  [Colony Nexus] Bootstrap complete - X method(s) patched, 1 extension(s) loaded

Whenever you post a marketplace listing in-game, the toast fires.

To customise:
  - Change the trigger conditions / toast text in OnMarketplace
    CacheUpdated.
  - Subscribe to more events on host.Events (Treasury / Quest /
    Guild / PlayerStats / LinkedAccounts / GuildLeaderboard cache
    updates, KmhServerConnected, KmhServerDisconnected).
  - Read more caches via host.Treasury, host.Quests, host.Guild,
    host.GuildLeaderboard, host.PlayerStats, host.LinkedAccounts.
  - Send custom wire kinds via host.Send and register handlers
    via host.RegisterHandler.
  - See EXTENSIONS.md in the addon repo for the full SDK reference.

Important: this template is a SEPARATE RimWorld mod, not a fork of
Colony Nexus. KMH's LICENSE permits SDK-using extensions (see LICENSE
Section 7) - you choose your own license for the extension itself.
