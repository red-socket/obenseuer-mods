# Changelog

All notable changes to Sound Sanity are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Volume slider fix: some world sounds are wired to a stray copy of the game's audio mixer, or to no mixer at all, so the volume sliders don't reach them. These include room and building upgrades, lifts, lift doors, keypads, fans, flickering lights, bells and shop previews. They now follow the Master and Sound sliders and go quiet while you sleep. Earmuffs muffle them like every other sound. Two optional extras, off by default, also cover the few lifts and props that only follow the Master slider, and residents' voices and footsteps.
- Loud machines get their own volume settings, in ten groups: crushers, grinders, saws, brewing, smithing, machining, generators, furnaces, workshop machines and everything else with a running sound. They all start at 100%, the game's own volume. Changes apply at once, even to machines that are running. Stoves and fireplaces count as "everything else", and you can move any machine to another group by its name.
- Radio music follows the Music slider: radios and record players you switch on, and the background radio and bar music in shops and flats (#5893). A radio music volume works on top of the Music slider, and separate mute switches cover the radios you switch on and background radio music. A muted radio stays on. Radios keep the game's own routing, so whether the Sound slider, sleep and earmuffs also reach a radio depends on where the game has put it: right after a level loads, they don't.
- Nothing goes into your save, so you can add or remove the mod at any point in a playthrough.
