KMH in-game button icons
========================

The icons RimWorld draws on KMH's tab + dialog buttons. They ship as 128px
coloured PNGs (Noto Emoji); the buttons draw them at ~22px, untinted, so colour
shows. Drop a replacement with the same name to override - ContentFinder also
accepts .dds / .dds.zstd.

Slots (filename = "KMHPatch/UI/<name>", extension doesn't matter):
  Guild       Quests       Marketplace   Treasury
  Leaderboard Buy          Post          Cancel
  Claim       Submit       Approve       Deposit
  Withdraw    Ping         Log           About

Missing files are fine - those buttons fall back to text. The boot log prints
"Textures: N/16 icons loaded" so you can confirm what resolved.
