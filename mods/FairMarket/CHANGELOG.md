# Changelog

All notable changes to Fair Market are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Shop doors show their opening hours: "open until 21:00" while open, and when the shop next opens while it's locked, with the week's hours underneath. Doors inside a building show "(Open) · open until 21:00", and the game's "(Close)" stays as it is. Market stalls that keep hours show "open until". The hours come from the game itself, so they follow any change to them. Nothing about the hours changes.
- The dishwasher shows its real fail chance to one decimal, the base chance, the machine's efficiency and the full wash time.
- The dishwasher lists the items it won't wash and says why: not stolen, a category it refuses, or a second stack of the same item.
- Invoice letters say when they are a scam (no penalty if you don't pay) or when they no longer match an open debt because they were paid, expired or copied. The tag also shows in the item panel, so it works with a controller.
- An Invoices list with every unpaid invoice the game has on record, what you owe and what you have. Pay has the same cost and effects as the invoice machine, but the letter isn't used up. Open it from the Notes page with Drop (R, or Y on a controller). A pause menu entry and a key are optional. Old scam letters are folded under one row. Dismissing invoices for 0 is opt-in. The list also fits small screens at a large UiScale.
- Optional, off by default: building materials (Rail, Leather Roll, Glass Pane, Insulation Wool, Car Battery, Electric Motor, Tarp and Agar) turn up more often in the shops you choose. The shops' other common goods turn up about a quarter less often at the General Trader. This changes the game's balance.
- Optional, off by default: the Mines canteen buys food, the butcher buys raw meat and fish eggs (paying in RM), and Ms. Lotina in Slum Shack 5 buys meat and fish eggs (stolen ones too, as she already does for raw meat), at the game's normal prices and within the money they have. You can set what each trader buys and add other traders.

### Fixed

- Selling to a trader who needs certificates could use a certificate made for a different item first, and could use more certificates than items sold (#5813). The certificate made for the item is now used first, then unlinked ones, and never more than the number of items sold. On by default, and you can turn it off.
