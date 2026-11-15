# Changelog

All notable changes to Save Guard are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- Crash-safe saves. The previous version of a slot stays on disk until the new save is completely written. A save that fails before it is complete is rolled back right away, and a save that fails after every file was written is kept. If a crash or forced quit cuts a save short, the next launch keeps the new version when it is complete and otherwise puts the previous one back. The other version goes to the recovered list.
- Rotating backups. After a save, the slot is copied to `SaveGuard/backups/<Character>/`, outside the game's `Saves` folder. Save Guard keeps 10 per character and makes at most one every 10 minutes, but always backs up saves into Slot 1-10.
- Pre-update backup. The first time a new game version starts, every character's saves are copied before the main menu appears. Save Guard's first run does the same.
- Damaged-save check. Loading a slot that looks damaged (it would fail to load, or some areas or conversations may reset) stops with Load anyway and Cancel. From the main menu it also offers to restore the newest good copy. The prompt works with a controller, and the main menu lists damaged slots.
- Restore window, opened with F6 (rebindable) in the main menu. It lists every backup, recovered copy and pre-update copy of a character, and restores any of them into its own slot or into any of Slot 1-10. The slot it replaces goes to the recovered list (10 replaced slots per character by default). In game, F6 shows the backup status.
- Optional timed autosaves, off by default. They save after N real minutes or N in-game hours without a save, into the game's own Autosave 1/2 rotation, and wait during dialogue, menus, sleep, lockpicking, police chases, prison, loading and other unsafe moments.
- All settings live in `BepInEx/config/redsocket.obenseuer.saveguard.cfg`.
