# Workshop art

Nothing is shipped as an image file. Everything the mod draws in game is a game asset used at runtime.

| Use | Game asset | Treatment |
|---|---|---|
| Fetch and repeat buttons | clones of the recipe panel's craft button (`RecipeInfoUI.createButtonEnabled`, the `createButtonPrefab` instance in `CraftButtonSlot`), Relay removed, controller glyph hidden | Unchanged look; text from `Core/Strings.cs`, TMP autosize 10-15. Fetch 166×40 (236×40 alone), repeat 64×40, in a row 6 px apart at y 84-124 of the RecipeInfo. Dimmed with a CanvasGroup (alpha 0.5) when there is nothing to fetch. |
| Extra vessel slot backdrops | clones of `SlotNotAvailable` under the vessel panel's `Manufacture inventory slots / Not available slots` | Named `WS_SlotNotAvailable`, 55×55, placed like the authored 9 (49.5 + 69·col, −49.5 − 69·row). |
| Texts with × and · | the game's TMP fonts | Checked: `Erika Ormig SDF` (all variants), `Roboto-Regular SDF` and `LiberationSans SDF` have U+00D7 and U+00B7 in their character tables. |

## Nexus images

**Status: not started.** Header, thumbnail and gallery follow the house kit (`art/SOURCES.md` at the repo root) once the in-game tests have passed. Suggested shots, test IDs from `.internal/test-plan.md`:

| # | Screenshot | Set up |
|---|---|---|
| 01 | `ws-fetch.png` | Table saw open on Planks, a crate of logs nearby, the "Fetch ingredients" button (F2) |
| 02 | `ws-vessel.png` | Mashing Barrel on Red Grape mash showing "Fetch for ×k" (F7) |
| 03 | `ws-repeat.png` | Table saw with a repeat running: "Stop (n)" and "Craft 5/5 (n)" (Q1) |
| 04 | `ws-hint.png` | Looking at a table saw with the menu closed: "U: repeat Planks" (Q4) |
| 05 | `ws-cancel.png` | Dishwasher with a running wash after the first click, the confirm note on screen (C1) |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
