# Changelog

All notable changes to No Floor Spill are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Stove ash no longer spills onto the floor once the ash slot is full. By default the extra ash is deleted. It can instead go into storage furniture you own near the stove, or drop as in the game. Electronic Waste and other refuse can be added by item ID. Refuse never goes into your inventory. When the slot holds a different item, the mod never deletes the incoming refuse: in NearbyStorage mode it goes to nearby storage, otherwise it drops as in the game.
- Leftovers a full machine gives back when an ingredient runs out (plastic trash, paper trash, empty bottles, petri dishes) no longer spill onto the floor (#6570). They go into your inventory while the machine's window is open, then into storage you own nearby. Whatever is left drops next to the machine as before. The slot the used-up ingredient freed stays free for the finished item, except on machines with a separate output storage.

### Known limits

- Ash from failed cooking at a full stove still drops as in the game.
