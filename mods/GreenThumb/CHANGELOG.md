# Changelog

All notable changes to Green Thumb are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Harvest all: keep holding Use on a planter, rack or field for 1.2 seconds and every ready plant within reach is harvested, nearest first, at the game's own pace. You get exactly what picking each plant by hand gives. Plants that belong to someone else are skipped unless you turn that on, and then each one counts as theft as usual. Works with a controller (hold A). The harvest prompt mentions the longer hold.
- Harvest all won't pull up a whole plant when its harvest wouldn't fit in your inventory, so a full field doesn't end up on the floor. The prompt flashes once instead.
- The quick-plant bar and plant preview no longer pop up next to plants that are ready to harvest (#6301). Other modes: bar without the preview, only while crouching, never, or as in the game.
- Produce of similar freshness now stacks. Stacks join within 10% bands, which break at 30% and 70%; as in the game, stacks less than 2.5% apart still join. The stack takes the average freshness of everything in it, weighted by amount. Merging never changes how many items you have. On by default, and you can turn it off, change the band width or include all food.
- Optional, off by default: fruit doesn't lose freshness while it waits on the plant. Choose 100% or a minimum such as 70%. This changes the game's balance.

### Fixed

- "Growing fruits" counted down as the plant aged. It now counts up to the next ripe fruit and shows 100% when there is fruit to pick. Plants that can't grow more fruit show "Spoiling" with the time the plant has left.
- Planters only checked their sunlight when a level loaded, so moving a cupboard over one kept "Sun" until the next door. They now check again every 10 seconds and right after you close the build menu.
- Growers that run on power (terrariums, aquariums, hydroponic, aquatic and vertical farms, the mushroom growing cabinet and the flowerbox with lamp) kept growing with no power or when switched off, while some planters that need no power could stop for good (#6273). They now stop while they have no power or are switched off and carry on when it comes back, and growers that don't use power never stop. Plants in a stopped grower say why. On by default, and you can turn it off.
