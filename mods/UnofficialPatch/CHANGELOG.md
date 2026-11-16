# Changelog

All notable changes to Unofficial Patch are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Fixed

- Biweekly rent is now collected every second Saturday. The game collected it every Saturday at the biweekly amount, which came to twice the monthly rent. Loan downpayments on a biweekly schedule follow the same calendar.
- A scam invoice paid into a frozen bank account now stays in that account, and the notification says the account is frozen. Before, the game opened a hidden second account with the same name and said the money had been added, but you could never get at it.
- Hidden duplicate bank accounts left by that bug are merged into the real account on load, so their money and debt show up again.
- While the F1 console is open, the keys you type no longer trigger game actions, and typing X or pressing Enter no longer clicks pause menu buttons (#5942).
- With toggle sprint or toggle crouch, sprinting and crouching now stay on after going through a door, sleeping or a jail transfer.
- Stills, mashing tuns and similar machines stuck with a tiny leftover of liquid fix themselves when you open them. Amounts just under 0.1 L are rounded up to 0.1 L so a container can drain them.
- Tanks and sinks holding only a trace of liquid are emptied, so another liquid can go in and they can be picked up, sold or dismantled again.
- Ice cream and ice cream cones keep in a freezer (#5905), and coffee stops going stale in a fridge. Ice cream and coffee you already have are corrected too.
- The Rexus tablet shelf in the Bazaar electronics store restocks again.
- Cats hunt rats again (#6213).
- Police open closed doors and the closed toll bridge gate as they reach them, and close them after they pass; before, they walked straight through. Where police can go hasn't changed, and locked doors stay locked. Locked doors and doors you can't open yourself, such as the toll gate, shut again right behind them, even if you are standing next to one. If you have just paid the toll, the gate stays open for you.
- Porta-potty and outhouse doors no longer close by themselves while you sit on the toilet, stand inside, or turn around after getting up. Pressing E on the door still closes it, and it still closes behind you when you walk away.

### Added

- A log warning when two actions you use at the same time share a key, or an action is bound to a key the game reserves (F1, F11, F12, 1-4).
- A log warning when a shop shelf refuses an item during restock.
- Optional, off by default:
  - Keyboard and mouse only: the game never switches to controller prompts or navigation, and ignores joystick axes in menus, on ladders, in the quick-slot bar and in the card game (#5811).
  - A deadzone for joystick axes in menus, and a higher threshold before the game switches to controller prompts. Ladders, the quick-slot bar and the card game are not affected.
  - Your own cats take no damage from crafting machines such as the Small Jaw Crusher (#5978).
  - Achievement Doctor: sends Steam the achievements your save says you earned but Steam never got (#6464), unlocks the "all apartments at level N" achievements you already qualify for (#6494), and resets achievement progress when you start a new game, so addiction-cured achievements no longer unlock at character creation (#6227).
  - Restore the game's current exchange and casino rates over older values stored in a save (a possible cause of #6537).
  - Apply your brightness setting again after loads and after the SMV and Wasteland visual effects.
  - Coffee maker volume: every brew gives 0.2 L, exactly one mug. The game brews 0.3 L, which wastes 0.1 L with a single mug but fills a second mug when you load two or more, so in that case this option gives less coffee per brew.
  - While your apartment walls are being renovated, apartment beds keep the safety your finished walls give, Teddy bear cameras included, and never more. Bed safety still depends on your walls upgrade.
  - The Crazy Neighbor, robbers and followers also open the closed doors they walk through. They open locked doors only when the game gives them a key.
- All settings are in `BepInEx/config/redsocket.obenseuer.unofficialpatch.cfg`. Every fix can be turned off on its own.
