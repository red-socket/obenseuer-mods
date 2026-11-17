# Better Placement art

No art is shipped as an image file. Everything the mod draws in game is made at runtime from the game's own resources.

| Use | Game asset | Treatment |
|---|---|---|
| Guide lines | a `LineRenderer` with a new material on the built-in `Sprites/Default` shader, the shader vanilla's crafting connection lines use (`CraftingConnectionIndicator.CreateLine`, SRC/CraftingConnectionIndicator.cs:43-57) | Width 0.01 m (`Align.GuideWidth`), view-aligned, no caps, no shadows. Colours from the config: centre lines `#4FC3F7CC` (light blue), edge and flush lines `#FFB74DCC` (amber). Layer 2, no collider. |
| Controls line segments | the building menu's own `controlsInfoText` (TextMeshProUGUI) | Text only, in vanilla's `KEY)ACTION   ` style and capitals; key names from `InputManager.GetKeyCodeDisplayText` ("Mouse 4", "Mouse 5"), "Left Alt" for LeftAlt (the game would print "LeftAlt"). The degree sign is already used by vanilla's own rotation mode text, so the font has it. |

The colours are placeholders until the house style settles; they were picked to read on the game's dark floors and walls and to stay apart from the red "can't place" material.

## Nexus images

Render with `mods/BetterPlacement/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline (the listing's own) went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/BetterPlacement/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. The in-game tests in `.internal/test-plan.md` have not run yet; a test result that changes the listing can change a caption too.

Tagline: "Straight rows without squinting." Key shot (header and thumbnail): `bp-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): furniture lined up neatly, for example the three Hydroponic Farms from 01 in a flush row, in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `bp-row.png` | `add_furniture Hydroponic Farm 3`. In building mode place two side by side, pick the third and hold Left Alt next to them so it snaps flush and the guide lines show. Screenshot before clicking. | Hold Alt and the piece snaps flush to its neighbours. |
| 02 | `bp-wall.png` | `add_furniture Small Wooden Cupboard 1`, `add_furniture Painting Fox 1`. Cupboard against a wall, then the painting above it with Left Alt held: the blue centre line. | On a wall, a painting lines up centred over a cupboard. |
| 03 | `bp-snap.png` | Placing any piece: hold the right mouse button to rotate and hold Mouse 4. The controls line at the bottom of the screen shows the 15° step. | Hold Mouse 4 or Mouse 5 to turn in 15° or 45° steps. |
| 04 | `bp-rug.png` (optional) | Game closed: in `redsocket.obenseuer.betterplacement.cfg`, `[FlatItems]`, set `Enabled = true` (restore afterwards). `add_furniture Cottage Table 1`, `add_furniture Rug Persian 1`; place the table, then the rug under it. | Optional: rugs can go under tables and chairs. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
