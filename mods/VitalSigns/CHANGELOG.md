# Changelog

All notable changes to Vital Signs are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Damage log: every health loss with its cause and amount, including the ones the game doesn't show. That covers the radiation dose, mushroom spores behind a mask, harmful shower water and the hidden damage queue behind caffeine overdoses, raw food, poison and other items. Each queued amount is named after the item or effect that caused it, and the fungal infection's health cap and the food penalty above SMV 40 get their own lines.
- A "Losing health now" summary: hidden damage still to come, how long bleeding lasts if untreated, the radiation dose and when it stops, the fungal health cap and whether caffeine is about to overdose. All figures use the game's own formulas.
- Short notes at the top right when one hidden cause costs 5 HP or more within an hour (both adjustable). While you sleep or wait, they are held and summed up afterwards. When you die, a note says what really caused it, since the game reports every hidden-damage death as poisoning.
- Mental health page: the exact breakdown of the mood shown on the HUD (underlying mood, alcohol and hygiene), where your underlying mood is heading and how long it takes, the cap your unmet needs put on it, your expectations, how strong antidepressants and good food are at your current mood, and how long each item you've used stays weaker.
- Addictions page: the level, need and recovery rate of each addiction, with the time until it wears off. The duck addiction gets its own readout.
- Extra lines in the stat tooltips for Health, Mental health, the four needs and the duck need.
- Optional: keep the Mental health icon on the HUD even when your mood is good (off by default).
- Controller support: on the Character page, Use Item (X, or A on a controller) opens Vital Signs, and the window works with a controller. A pause menu entry and a hotkey are optional (both off by default).

### Fixed

- The gambling need now counts toward unmet needs on its own. The game only counted it while you also craved smokes. On by default, and you can turn it off.
- Alcohol and mushroom addiction stay between 0 and 100. The game let them go past 100 and left mushroom addiction at -0.1 after Methanol. Saves that are already outside that range are corrected when they load. Both are on by default, and you can turn either off. An optional setting, off by default, also caps smoking addiction at 100 (a Vape Liquid can leave it at 125).
- Item fatigue no longer stacks without limit. Each dose leaves a medicine weaker for 100 game hours, and the game added another 100 hours for every extra dose, so a few doses too many could make antidepressants useless for months. Good food and drinks build up the same fatigue (drinks from the same container, such as a bottle or can, share one count). Vital Signs caps each item at 2 doses' worth: after any number of doses, an item has no effect for at most 100 game hours and is back to full strength within 200. A second dose right after the first still does nothing, as in the game. If a save already has more, Vital Signs lowers each item to the cap when the save loads. On by default, and you can change the cap or turn it off.
- Duck craving follows game time instead of your frame rate while you're awake, so the game's own fast waits (TV, benches, jobs, jail) count like normal time. Sleep is unchanged. On by default, and you can turn it off.
