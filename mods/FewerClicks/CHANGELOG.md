# Changelog

All notable changes to Fewer Clicks are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Top up: a button in storage windows that moves matching items from your inventory onto stacks the storage already has. It stops when those stacks are full and never fills an empty slot (#5826). The button is hidden during trades. A hotkey (unbound by default) does the same, and on a controller you reach the button with Left from Sort. Taking from the worn backpack and filling new stacks are options, both off by default.
- Auto Swing: hold Primary Use on a tree, ore, rock, limestone or fatberg and the tool keeps swinging after each normal cooldown. Every swing costs the same time and tool wear as a click. It only starts a swing at a resource the tool can work, never at people, empty nodes or the wrong kind of resource, and by default skips resources that belong to someone else. If someone steps in front of a swing it started, that swing misses. A toggle key can be bound.
- Quick Eat & Drink: press 5 (or D-pad Down) to eat and drink from your main inventory. The game's own auto-eat picks the food, and by default it skips alcohol, mushrooms and other food with side effects. It does nothing in menus, dialogue, shops or trades, or while sleeping. In third person, D-pad Down scrolls the list of nearby objects while it is on screen.
- Auto Eat (off by default): eats and drinks on its own once hunger or thirst reaches 50.
- Slot keys (off by default): keys 6 to 9 use the food, drink, smokes, medicine or bandages in the top row of your inventory.
- Fill all: a button in sink, tap and barrel windows that fills every container in your main inventory one after another, the same way pressing Fill on each one does. Speed, the source's liquid and the theft rules stay as in the game, and by default the run stops after the current container if someone sees you taking from a source that isn't yours.

### Known limits

- Fill all needs a mouse or keyboard: a controller can't reach the button. On a controller, X still fills one container at a time, as in the game. A keyboard key or a controller button can be bound as the Fill all hotkey, but every controller button already does something in that window.
