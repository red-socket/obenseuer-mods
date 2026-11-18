# Changelog

All notable changes to Message Log are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Every notification the game shows is logged with the in-game day and time, including the ones our other mods send through the game. A message that repeats within 3 seconds is counted (×2, ×3) instead of listed again, and an objective's counter (3/10) keeps only its latest step between saves.
- What people say to you in conversations is logged with their names, and so is the reply you picked, shown as "You". Both can be turned off.
- The log is kept next to each save, in `MessageLog.txt`, so loading a save brings back the messages that save knew about. A line marks where messages are missing, for example after playing without the mod.
- A Message Log window, opened from the journal's Notes page with Move Item (F, or X on a controller). It lists the newest messages first, with filters for crime, money, mail, quests, dialogue and everything else, a search box, and "Only this" (R, or Y on a controller), which shows only messages from the same person or of the same kind. It works with a keyboard, a mouse and a controller. A pause menu entry and a key are optional.
- With the Obenseuer PT-BR translation installed, the window shows messages in Portuguese, the way the game shows them, and the search finds both the English and the Portuguese words.
