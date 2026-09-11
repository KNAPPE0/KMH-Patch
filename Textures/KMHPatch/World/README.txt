KMH world-map marker art
========================

The markers KMH draws on the world map: production sites, guild halls and Frontier
outposts. Separate from Textures/KMHPatch/UI, which is button art for the KMH tab.

Two sets, and they are NOT interchangeable
------------------------------------------

RimWorld draws a world object from two different images:

  WorldObjects/<Name>            the small always-visible dot on the map
  WorldObjects/Expanding/<Name>  the larger icon shown when you zoom out

Every vanilla WorldObjectDef ships both and pairs them (World/WorldObjects/Camp with
World/WorldObjects/Expanding/Camp, and so on). KMH used to point BOTH slots of KMHSite
and KMHGuildHall at a single vanilla quad texture, which is what made KMH markers read
wrong on the world map. Contract check section 43 now fails the build if any KMH def
puts the same image in both slots, names a non-Expanding path in the expanding slot,
uses vanilla art, or names a file that does not exist.

Style: white silhouettes on transparent
---------------------------------------

Like vanilla's own expanding icons, and for a concrete reason - KMH tints markers by
state (claimable gold, hostile red, defeated grey, dormant dimmed, captured by owner).
A coloured icon fights that tint and the colour stops meaning anything. The archetype
markers previously used the coloured Noto Emoji button icons and had exactly this
problem: a hostile marker could not actually turn red.

Colour is never the only signal. Anything a player must not miss also changes shape or
draw priority - see KmhMarkerArt.ColorFor / PriorityFor.

Drawn for 22 pixels
-------------------

The map draws these at roughly 22px. Anything thinner than ~8px in the 128px source
disappears, and two shapes closer than ~6px merge into one blob. One dominant
silhouette per glyph, and check every new one at 22px before shipping it.

Watch for collisions with the rest of the set, not just legibility. Farmland went
through four rejected drafts for that reason alone: a banded field mirrored Quarry's
pit, a grain ear read as one of Woodland's conifers, a sickle read as a refresh arrow,
and a tied sheaf read as an hourglass. The sprout's ROUND leaves are what separate it -
Woodland's conifers are triangles, and nothing else in the set is round.

Slots
-----

  Farmland    sprout on tilled ground
  Quarry      terraced pit
  Woodland    two conifers
  Ranch       quadruped over a rail
  Roadworks   road narrowing into the distance
  Custom      plain works building
  GuildHall   hall under a banner       (a seat, deliberately not a work site)
  Outpost     watchtower between stakes
  Generic     KMH's diamond             (fallback, and the mark chat uses for KMH sources)

Regenerating
------------

Same pipeline as the UI icons - see Textures/KMHPatch/UI/README.txt for the exact
texconv flags, the miscFlags2 fixup and the streaming-zstd settings. Every file here
is 128x128 BC7_UNORM_SRGB, 8 mips, straight alpha, and ships as .png + .dds + .dds.zstd
like the rest of the set.

Orientation - every .dds is stored FLIPPED vs its .png
-----------------------------------------------------

RimWorld's DDS loader reads rows BOTTOM-UP; texconv writes them top-down. So a .dds
whose pixels match its .png renders UPSIDE DOWN in game. The .png files here are all
the right way up - the flip belongs in packing, and the packer does it.

This was measured, not deduced. All 20 shipped UI icons were decoded back to PNG:

  14 stored flipped -> render correctly
   4 stored unflipped -> Comms, Roadworks, Sites, World
   2 vertically symmetric -> undecidable, and it cannot matter

Those 4 are exactly the ones a live test reported upside down. They have been repacked.

Contract check section 43 now decodes every KMH .dds and fails the build if any of them
is not stored flipped relative to its .png. It decodes per source FOLDER, because
Roadworks.dds exists three times (UI, WorldObjects, WorldObjects/Expanding) and texconv
writes output by base name - a flat temp folder silently overwrote two of them.

An earlier attempt compared KMH's own two art sets against each other. That was chasing
a symptom: it could not see the problem on a near-symmetric glyph, and it did not explain
the UI icons at all.
