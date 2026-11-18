# Field Guide art

Nothing in this folder ships: the mod draws no art of its own. The pop-ups use the game's own hint panel (`HintOverlay`: title, bulleted body, the "Hide hints" key button), and the Bought-by line is text inside the game's own tooltip and item panel.

| Use | Game asset | Treatment |
|---|---|---|
| Station guides | the game's hint panel (`HintOverlay`, the same panel as the card-game rules) | Title, bullets and a plain closing line, built with `HintContent`. Bullets follow the rules hints' style (sentence case, no full stop). |
| "Bought by" (tooltip) | the item tooltip's details text (`TooltipUI.details`, TMP, rich text) | Bold header like the vanilla "Required in" block; names in `<noparse>`; " · " (U+00B7) between traders. |
| "Bought by" (item panel) | `ItemInfoPanel.itemDescripion` (TMP, rich text off) | Plain text under the description; dropped when it doesn't fit. |

## Nexus images

Not made yet. When they are, follow the house kit (`art/STYLE.md` and `art/SOURCES.md` at the repo root) and Fair Market's `art/render.sh` pattern, and record each screenshot and crop here. Proposed shot list (captions go through the humanizer):

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `fg-mash.png` | Open a Mashing Barrel with ingredients in it. | How the mash tun works, right where you need it. |
| 02 | `fg-sauna.png` | A built basement sauna, stove panel open. | Sauna basics, from the stove. |
| 03 | `fg-tooltip.png` | Hover a valuable after visiting the Bazaar (Pawn Shop, Brom's). | Who buys it, and for how much. |
| 04 | `fg-panel-pad.png` | Controller, inventory item panel on an item with a one-line description. | Works with a controller. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
