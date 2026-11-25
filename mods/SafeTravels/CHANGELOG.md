# Changelog

All notable changes to Safe Travels are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - 2026-11-25

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Toll bridge lock: after you pay at the Tenement toll bridge, the coin slot takes no more money until you've walked through or the gate has reset, so a quick double press no longer costs 20 OC. The prompt turns green and reads "Paid - walk through". The toll still costs its price once per crossing, and only the toll bridge slot is locked. If the gate can't be found, the slot still refuses coins for 3 seconds after you pay (LockSeconds). Optional Price setting; -1 keeps the game's 10 OC.
- Get Unstuck: a pause menu entry and an optional key that move you back to a spot where you recently stood on solid ground. If there's none, it uses an entrance within 20 m, but not the one you're standing at. Each press walks further back along your trail. It won't put you in a private area, and it's meant to keep you on your side of the toll gate and out of closed shops and the jail entrance. It doesn't work while you're falling, seated, on a ladder or the minitrain, or while you're talking to someone or have the inventory or another menu open, and it's also blocked in jail, during police chases, cutscenes, minigames, waits and loading. One use per minute of play by default; time in the pause menu doesn't count.
- Minitrain coupling: trains only couple on slow contact that you cause, while you drive one, within 5 seconds of stepping off, or when you pull a coupler's "Enable Coupling" lever while it touches another train. Parked trains no longer couple when an area loads, and reversing into a wagon faster than 2.5 m/s no longer couples it. Couplings you make are saved and loaded as usual.
- Minitrain decoupling: a coupling only breaks when the gap stays too wide for 5 frames and 0.2 seconds, not on one bad frame at a switch, a curve or an autosave. A gap twice the limit still breaks it at once, and the coupler lever still uncouples right away.
- Minitrain consists (#5988): after a load, a coupling or an uncoupling, Safe Travels checks that each train's wagon list matches its couplings and repairs a mismatch with the game's own rebuild, so uncoupled wagons stop following the locomotive.
- Rerail, off by default (Rerail.Enabled): press End to reset a locomotive onto its rails where it stands, using the game's own reset. It hasn't been tested on a real derail, so it may not fix every one. It picks the locomotive you're looking at, or else the last one you drove or the nearest one within 15 m. Its wagons are uncoupled first and stay where they are. The train has to stand still with its engine off. A pause menu entry for controllers is also available (Rerail.PauseMenuButton).
- Settings for every feature, each with its own switch. The mod adds nothing of its own to your save (only ordinary game values change), so it's safe to add or remove mid-playthrough.
