# Changelog

All notable changes to Recipe Book are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- A search bar above the recipe list of every crafting station. Type part of a recipe's name, or of an ingredient or product ("lettuce"), and the list narrows as you type. Case and accents don't matter. While you type, game keys are blocked, so letters don't move you, switch pages or drop items.
- A Show button next to the search box: All, Ready (you can craft it now), Skill met, and one entry per skill on stations that mix skills. It shows how many recipes are listed. The recipe you have open always stays in the list.
- Controller support: press Up on the top recipe to reach the bar (this works with a recipe open too), Left and Right to change Show, A to reset the search and the filter, and Down to go back. The controls line shows the buttons. On a keyboard, Use Item on the bar puts the cursor in the search box.
- Badges on recipe buttons with the fail chance (the same number as the recipe panel, amber from 25% and bold from 50%) and your skill level against the level needed. "!" marks recipes the game won't let you craft until you reach that level.
- Item tooltips and the inventory's item panel list the recipes that use an item. By default only recipes you can already see at a station are listed. A filled bottle lists the uses of what's inside.
- Magazines and books show how many of the recipes they can teach are still new to you, and how many of those would unlock a new craft. They show only the numbers, not the recipe names.
- Optional, off by default: magazines and books always pick a recipe you don't have while any are left (the game does this 80% of the time). This changes the game's balance.
- Optional, off by default: recipe papers lying in the world swap a recipe you already have for a new one from the same paper's list. Only papers rolled from then on change. This changes the game's balance.
