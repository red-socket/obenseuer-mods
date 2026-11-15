# Changelog

All notable changes to Fair Justice are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- Sentence panel while you are in jail (F10 shows or hides it): time left until release with the expected clock time, or "minimum stay" / "maximum sentence", and the charges by crime type.
- Optional: a release that falls due at night waits until morning (`NightRelease.Enabled`, off by default).
- Release grace: for 60 seconds of play after you serve your time, the jail's own areas don't count as trespassing. Any new crime ends it at once. The Bazaar office and warden's room stay off limits.
- Optional: during the release grace, police in every level ignore sightings of you, unless you carry stolen goods or crimes they would question you about (`ReleaseGrace.PoliceAnyScene`, off by default).
- Optional: the warden's line about the orange bag plays when Fair Justice bagged your items (`Confiscation.WardenMentionsBag`, off by default). The game never clears this flag, so the warden keeps mentioning the bag afterwards, even without the mod.
- Optional: the open-cell-door grace also applies once after loading a save made in jail (`CellDoorGrace.AfterLoadInJail`, off by default).
- Optional: after each load, add the stolen mark to stolen stacks you carry that lack it (`Theft.RepairOnLoad`, off by default).
- All settings are in `BepInEx/config/redsocket.obenseuer.fairjustice.cfg`. Every feature can be turned off on its own.

### Fixed

- Moving stolen items between your inventory, worn backpack and equipment no longer counts as a new theft and no longer makes nearby people turn their heads (#3539). Putting your own items into other containers makes no noise either.
- The stolen mark now goes on the stacks that actually received the items, not on inventory slot 1, so stolen stacks keep their mark and stack with each other again.
- On arrest, items that might not fit your cell locker now go into the jail's orange bag instead of falling on the floor, where they despawned. Stolen goods are still confiscated.
- When a stolen backpack you wear is taken (arrest search or paid fine), your own items inside move to your inventory first.
- Paying a fine you cannot afford no longer takes your stolen goods anyway.
- Stepping through the still-open cell door right after being locked up is no longer trespassing (#5847).
- Police pursuits are called off when you are released, so officers no longer chase you straight back in (#6183).
- Passing out in jail no longer gets you arrested a second time (#5543). Stolen goods are still taken.
