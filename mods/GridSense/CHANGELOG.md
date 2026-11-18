# Changelog

All notable changes to Grid Sense are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- A "Power:" line in the furniture info of shops and the workbench, when you buy, sell, craft or dismantle. Crafting stations show what they draw while crafting and while idle, or say that they draw the same either way. Lamps, fridges, TVs and growers show their draw. Generators and battery banks show what they supply, and battery banks also show what they draw while charging. The numbers are the ones the game uses for your grid and your bills.
- The same wattage after the furniture name in build mode, for example "Bandsaw - 1.3 kW (300 W idle)".
- The same wattage on the furniture display stands in Ankea and O-Iron. When you look at a stand, the wattage comes after the price (and the stock, at O-Iron), for example "- 50 W" for a Banker's Lamp. Prices and stock don't change.
- An option to stop the lamp flicker that means you have no electricity contract, for players who find it hard on the eyes. It's off by default. Lamps on a fuel generator still flicker, and other flickering lights in the world don't change.
