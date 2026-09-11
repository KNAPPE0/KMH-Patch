KMH in-game button icons
========================

The icons RimWorld draws on KMH's tab + dialog buttons. They ship as 128px
coloured PNGs (Noto Emoji); the buttons draw them at ~22px, untinted, so colour
shows. Drop a replacement with the same name to override - ContentFinder also
accepts .dds / .dds.zstd.

Slots (filename = "KMHPatch/UI/<name>", extension doesn't matter):

  Destinations   Guild       Quests       Marketplace   Treasury
                 Sites       Roadworks    Comms         World
                 Leaderboard
  Actions        Buy         Post         Cancel        Claim
                 Submit      Approve      Deposit       Withdraw
  Tools          Ping        Log          About

One icon per first-class destination; actions are shared across dialogs by meaning
(Deposit = value in, Withdraw = value back, Claim = take a slot, and so on), so the
same glyph means the same thing everywhere.

Missing files are fine - those buttons fall back to text. The boot log prints
"Textures: N/20 icons loaded" so you can confirm what resolved.

Every slot ships in all three forms
-----------------------------------
Each icon is present as .png, .dds and .dds.zstd, and every .dds is the same shape:

  128x128, BC7_UNORM_SRGB, DX10 header, 8 mip levels, straight alpha, 22020 bytes

Regenerating a slot. texconv ships with RimPy under compressors/:

  texconv.exe -f BC7_UNORM_SRGB -m 8 -alpha -y <Name>.png

FLIP THE IMAGE VERTICALLY FIRST. RimWorld's DDS loader reads rows bottom-up while
texconv writes them top-down, so a .dds that matches its .png renders upside down.
Comms, Roadworks, Sites and World were once repacked without this and all four showed
up inverted in game. Contract check section 43 now fails the build on it.

then set miscFlags2 (DX10 header, byte offset 144) to 1 so the alpha mode reads
STRAIGHT like the rest, and compress with zstd level 3 in STREAMING mode with
content-size, checksum and dictID all off - a one-shot compress picks a smaller
window and produces a different (still valid) frame. Python 3.14's stdlib does it:

  from compression import zstd
  P = zstd.CompressionParameter
  c = zstd.ZstdCompressor(options={P.compression_level: 3, P.content_size_flag: 0,
                                   P.checksum_flag: 0, P.dict_id_flag: 0})
  out = b"".join(c.compress(dds[i:i+8192]) for i in range(0, len(dds), 8192))
  out += c.flush(zstd.ZstdCompressor.FLUSH_FRAME)

Every shipped .dds.zstd is produced exactly this way, so a regenerated slot is
byte-identical to the rest of the set.
