# Changelog

All notable changes to Better Tenement are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- Stolen materials first. Contractor renovations take stolen stacks from the resource chest first, then stolen stacks you carry (optional), then clean chest stacks, then your inventory. A notification says how many stolen materials were used.
- Stash button on the five contractor resource chests (left of Sort; also the Insert key and Left from Sort on a controller). It moves every renovation material from your inventory and worn backpack into the chest, never from your hands or equipment, and never drops anything. It waits while an item is being dragged.
- Bigger resource chests: 176 slots by default (88-352 in the config). Storages with more than 88 slots scroll by whole rows (mouse wheel, PageUp/PageDown, or automatically with the controller), or show pages. A line under the chest title shows how full it is. `ResourceChests.PrepareForRemoval` shrinks the chests back to 88 slots (without deleting anything) before you remove the mod, and works even with the chests feature off.
- Upgrade cards mark counts that come from another contractor's chest at your last visit with "~", and turn amber when your carried items will be used.
- Landlord Ledger. Upgrade cards show the value and rent change per payday and who does the tier; floor plan tooltips show the next tier; a panel beside the Tenement tab (F3 collapses or expands it) shows rent per payday, occupancy, running renovations and a shopping list for starred upgrades.
- Tenement Doctor (F4): lists and outlines the leftovers that keep an apartment from counting as cleared, with the cleared percentage of each group next to the 90%/80% requirement. Picking leftovers up from the window is opt-in (off by default). With `Doctor.ForceClearMode = TakeOrDelete` it can also delete leftovers the game marks as not takeable: each delete asks first, is refused for anything holding items, and is permanent.
- All settings are in `BepInEx/config/redsocket.obenseuer.bettertenement.cfg`. Every feature can be turned off, and the over-charge fix (`Materials.FixRemainder`) has its own switch, separate from stolen-first.

### Fixed

- Renovation timers below 1x speed now count down in normal play, and 1.25x, 1.5x and similar speeds now finish faster than 1x (the game dropped the fraction of every tick).
- Renovation materials are charged correctly when more than one resource chest serves a contractor (the game charged each chest the full amount). The game itself has one chest per contractor, so this matters only with mods that add or link chests.
- Other contractors' chest counts on upgrade cards now update when you close a resource chest or save, not only when you talk to that contractor.
