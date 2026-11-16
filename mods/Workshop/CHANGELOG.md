# Changelog

All notable changes to Workshop are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Fetch ingredients: crafting stations and brewing vessels get a "Fetch ingredients" button above Craft or Start. It moves what the selected recipe is missing into the station from your inventory, the bag you're wearing and your own storage within 10 m on the same floor. Stolen items, other people's things, shop stock, contractor chests and Tidy Inventory's pinned slots are never touched. It handles ingredient groups (any yeast, any grapes), liquids in cans and bottles (whole containers move, and the game takes only the litres it needs), and the least fresh food goes first. On a brewing vessel it fetches for the biggest batch that fits and can be found. When something can't be found, it brings the ingredients it can and tells you what is still missing (#4947).
- Repeat last recipe: machines remember the last recipe you made on them. Look at one and press U to make it again (#6150). On machines that run without you, a repeat count button next to Fetch sets how many to make (1, 2, 3, 5, 10 or All). The rest start as slots free up, and the repeat stops when an ingredient runs out. Every start still needs the real ingredients, power, skill and free slots, and a repeat starts its next job only when the machine has room for what it makes.
- Cancel a running job: click a running dishwasher wash twice to cancel it. The item comes back with the same amount, charges and freshness, a stolen item stays stolen, and the powder is used up. You get no Cleaning skill for it (#5975). An option extends this to every machine, where you get the failed result instead. Jobs whose failed result includes something they also make, like coffee or planks, can't be cancelled.
- Optional, off by default: Jaw Crushers that crush loaded rocks on their own, and Bigger Vats, which gives the 100 L mashing tun, the industrial tun, the 1000 L fermenter and the distilleries more ingredient slots (#5913).
