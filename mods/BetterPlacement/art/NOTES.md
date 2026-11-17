# Better Placement art

No art is shipped as an image file. Everything the mod draws in game is made at runtime from the game's own resources.

| Use | Game asset | Treatment |
|---|---|---|
| Guide lines | a `LineRenderer` with a new material on the built-in `Sprites/Default` shader, the shader vanilla's crafting connection lines use (`CraftingConnectionIndicator.CreateLine`, SRC/CraftingConnectionIndicator.cs:43-57) | Width 0.01 m (`Align.GuideWidth`), view-aligned, no caps, no shadows. Colours from the config: centre lines `#4FC3F7CC` (light blue), edge and flush lines `#FFB74DCC` (amber). Layer 2, no collider. |
| Controls line segments | the building menu's own `controlsInfoText` (TextMeshProUGUI) | Text only, in vanilla's `KEY)ACTION   ` style and capitals; key names from `InputManager.GetKeyCodeDisplayText` ("Mouse 4", "Mouse 5"), "Left Alt" for LeftAlt (the game would print "LeftAlt"). The degree sign is already used by vanilla's own rotation mode text, so the font has it. |

The colours are placeholders until the house style settles; they were picked to read on the game's dark floors and walls and to stay apart from the red "can't place" material.

## Nexus images

**Status: not started.** Header, thumbnail and gallery follow the house kit (`art/SOURCES.md` at the repo root) once the in-game tests have passed. Suggested shots, test IDs from `.internal/test-plan.md`:

| # | Screenshot | Set up |
|---|---|---|
| 01 | `bp-row.png` | Two Hydroponic Farms in a row with a third being aligned flush, guide lines visible (A2) |
| 02 | `bp-wall.png` | A painting aligned above a cupboard, centre guide visible (A5) |
| 03 | `bp-hint.png` | The building menu controls line with the ALIGN and SNAP segments (K1) |
| 04 | `bp-rug.png` | A rug under a table and chairs with FlatItems on (F2) |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
