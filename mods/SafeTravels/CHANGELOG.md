# Changelog

All notable changes to Safe Travels are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Toll bridge lock: after you pay at the Tenement toll bridge, the coin slot takes no more money until you've walked through or the gate has reset, so a quick double press no longer costs 20 OC. The prompt turns green and reads "Paid - walk through". The toll still costs its price once per crossing, and other coin slots and slot machines work as before. If the gate can't be found, the slot still refuses coins for 3 seconds after you pay (LockSeconds). Optional Price setting; -1 keeps the game's 10 OC.
- Get Unstuck: a pause menu entry (it works with a controller) and an optional key that move you back to a spot where you recently stood on solid ground. If there's none, it uses an entrance within 20 m. Each press walks further back along your trail. It never takes you across the toll gate, into a shop that's closed or a private area, or to the jail entrance. It doesn't work in jail, with the police after you, while falling, seated, on a ladder or the minitrain, in dialogue, cutscenes, menus, minigames or waits, or during loading. One use per minute of play by default; time in the pause menu doesn't count.
- Minitrain coupling: trains only couple on slow contact that you cause, while you drive one, within 5 seconds of stepping off, or when you pull a coupler's "Enable Coupling" lever while it touches another train. Parked trains no longer couple when an area loads, and reversing into a wagon faster than 2.5 m/s no longer couples it. Saved couplings and train shop purchases are unchanged.
- Minitrain decoupling: a coupling only breaks when the gap stays too wide for 5 frames and 0.2 seconds, not on one bad frame at a switch, a curve or an autosave. A gap twice the limit still breaks it at once.
- Minitrain consists (#5988): after a load, a coupling or an uncoupling, trains whose wagon list no longer matches their couplings are repaired with the game's own rebuild, so uncoupled wagons stop following the locomotive.
- Rerail: press End to put a locomotive back on its rails where it stands, using the game's own reset. It picks the locomotive you're looking at, or else the last one you drove or the nearest one within 15 m. Its wagons are uncoupled first and stay where they are. The train has to stand still with its engine off. A pause menu entry for controllers is available but off by default (Rerail.PauseMenuButton).
- Settings for every feature, each with its own switch. The mod adds nothing of its own to your save (only ordinary game values change), so it's safe to add or remove mid-playthrough.
