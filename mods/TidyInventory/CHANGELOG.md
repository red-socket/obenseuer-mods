# Changelog

All notable changes to Tidy Inventory are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Pinned Slots: with the inventory open, press the middle mouse button over a slot to pin it to the item in it. Sort leaves pinned slots alone, a pinned stack can't be sold, and by default it can't be dragged, quick-moved, dropped, or moved by Top up, Stash, the amount slider or a controller. When the stack runs out, the slot stays reserved for that item: other items don't go there, and new copies of the item go there first. A filled bottle that you drink empty stays pinned. A padlock mark shows on pinned slots, and an empty reserved slot shows a faded picture of its item. Using, eating, equipping, filling and emptying, quests, crafting and confiscation all work on pinned stacks as before. With every other slot full, shops, pickups and harvesting report a full inventory, while quest and dialogue rewards are still given and drop at your feet. Pins are saved with your game.
- Empty button: trash bins, dumpsters and the Trash Collection Silo get an "Empty" button (#6330). Click it twice to throw away what's inside. Quest items, keycards, items that belong to someone else and stacks worth 500 or more are kept. Bins that belong to someone else get no button. On a controller, press Right from Take all to reach it.
- Combine: a Combine button next to the inventory's Sort button and in storage windows merges part-used items of one kind (matches, pills, glue, ink and so on) into full ones plus one part-used one. Every charge is kept, and each emptied item leaves the same leftover it would leave when used up, such as an empty bottle. Stolen items and items that belong to someone else are never combined. Items that sell at full price however much is left are skipped by default.
- Stolen stacks: stolen items from the same owner now stack in your inventory even when only some of them carry the stolen mark (#5030, #4848). The stack stays marked. Items from different owners never stack, and stacks inside a storage are left as they are.
- Blue rubber duck: once Mr. Duck's quest is done, you can throw the blue rubber duck away. Hover it in your items and press Delete twice, or select it with a controller and press A twice. By default this doesn't unlock the "duck addiction cured" achievement.
- Optional, all off by default: throwing away a single stack in a bin with Delete, emptying your own placed bins once a day, and keeping valuable dropped items from disappearing. The two bin options work without the Empty button.
