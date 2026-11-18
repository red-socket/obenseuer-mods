# Grid Sense art

Nothing in this folder ships: version 0.1.0 draws no art of its own. Every visible part is plain text added to the game's own UI.

| Use | Game asset | Treatment |
|---|---|---|
| "Power:" line in shops and at the workbench | the info panel's description text (`FurnitureInfoUI.currentDescription` in the shop and workbench UI) | Appended as the description's last paragraph, in its own font, 24 pt style and colour. No new object. |
| Build-bar wattage | the build menu's selection name (`BuildingSystemMenu.currentSelectionText`) | Appended to the name text (" - 1.3 kW (300 W idle)") in its own style. |
| Display-stand wattage | the look-at name text (`UseText.objectName`) on Ankea, Mallment and O-Iron stands | Appended to the name text (" - 50 W") in its own style. |

## Nexus images

**Status: not started.** Header, thumbnail and gallery follow the house kit (`art/STYLE.md`, `art/render.py` at the repo root) once the in-game tests have passed. The header could use the game's power meter or a lamp and generator from the furniture shop; record each source path in the table below. Captions and the tagline go through the humanizer before rendering.

Tagline (draft): "Know what a lamp, fridge or kiln will draw before you buy it."

Suggested gallery shots (test IDs from `.internal/test-plan.md`):

| # | Screenshot | Set up | Caption (draft) |
|---|---|---|---|
| 01 | `gs-shop.png` | T2: Table Lamp in Interior Bazaar Electronics | Wattage in the shop's info panel. |
| 02 | `gs-workbench.png` | T3: Bandsaw on the dismantle tab | Stations show crafting and idle draw. |
| 03 | `gs-build.png` | T5: Coffee Machine on the build bar | The same numbers in build mode. |
| 04 | `gs-settings.png` | ConfigurationManager with the Accessibility section | An option to stop the no-contract flicker. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
