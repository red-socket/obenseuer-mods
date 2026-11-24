# Changelog

All notable changes to Quest Helper are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - 2026-11-24

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Quest Doctor: a "Quest Helper" entry in the pause menu opens a window that checks your save for known stuck quests and can fix them. It covers the Rat problem rats that can't all be caught (#6547), the Casino Coup boss who isn't expecting you (#6071), Toivo's Collapse hazard after you told him it's not your tenement, Tanja's mining permit talk cut off halfway, and Eliana's first passport talk cut off halfway. A fix only shows up when your save is really in that state, it backs up your save first (into Save Guard's restore list if you use Save Guard), and it asks you to press twice. Fixes put the story back on track; they give no rewards, money or skipped steps. Problems in quests you don't have yet stay unnamed until you turn hints up to "Where".
- Task list in the same window: every task and objective with its status. An advanced option, off by default, lets you finish, fail or abandon any active task or objective, with the same backup and confirmation. Finishing skips whatever the objective asks for, and Quest Helper won't finish an objective the game wouldn't count as done yet.
- Journal additions: under each active objective in the task list, a line saying where and from whom you got it.
- Hints, off by default: pick how much you want to know (a nudge, who and where, or step by step) for the passport, mining permit, mine elevator and other main quests, with the fees and items you still need.
- Bazaar access page: the checkpoints between the tenement, the Bazaar and the mines and what each one needs, plus where your own passport and permit chain stands.
- Map pin: one pin on the map for the task you last opened, either from the hints or where you got the objective. It is never saved.
- To be continued: tasks that have reached the current end of their story move into their own section of the task list, with a clearer label than "cannot be completed" and a short note when it happens.
- Controller support: the pause entry, the window and the To be continued section are made to work with a controller too (tested so far with keyboard and mouse only). On the Tasks page, Use Item opens Quest Helper on the selected task and Drop folds the To be continued section.
