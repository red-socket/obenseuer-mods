# Grid Sense art

Nothing in this folder ships: version 0.1.0 draws no art of its own. Every visible part is plain text added to the game's own UI.

| Use | Game asset | Treatment |
|---|---|---|
| "Power:" line in shops and at the workbench | the info panel's description text (`FurnitureInfoUI.currentDescription` in the shop and workbench UI) | Appended as the description's last paragraph, in its own font, 24 pt style and colour. No new object. |
| Build-bar wattage | the build menu's selection name (`BuildingSystemMenu.currentSelectionText`) | Appended to the name text (" - 1.3 kW (300 W idle)") in its own style. |
| Display-stand wattage | the look-at name text (`UseText.objectName`) on Ankea, Mallment and O-Iron stands | Appended to the name text (" - 50 W") in its own style. |

## Nexus images

Render with `mods/GridSense/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080, with the tagline under the title) and one `gallery-NN.jpg` per shot, in the order below. Template elements come from the kit: `art/STYLE.md` and `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/GridSense/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. A test result that changes the listing can change a caption too.

Tagline: "See what furniture will draw before you buy it." The listing's "Know what a lamp, fridge or kiln will draw before you buy it." is too long for the thumbnail at full size, so the images shorten it. Key shot (header and thumbnail): `gs-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): your flat at night (`change_time 22`) with lamps on and a fridge, in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `gs-shop.png` | `change_scene Interior Bazaar Electronics`, select a Table Lamp at the counter: "Power: 60 W" in the info panel. | Shops show how much power furniture draws. |
| 02 | `gs-build.png` | `add_furniture Coffee Machine 1`, build mode, select it: "Coffee Machine - 1.3 kW (130 W idle)". | Build mode shows the wattage next to the name. |
| 03 | `gs-workbench.png` | `add_furniture Bandsaw 1`, then your workbench's Dismantle tab, select the Bandsaw: "Power: 1.3 kW crafting, 300 W idle". | The workbench shows crafting and idle draw. |
| 04 | `gs-stand.png` (optional) | `change_scene Interior Bazaar Ankea`, look at the Banker's Lamp display stand: "... - 50 W". | Display stands in Ankea and O-Iron show it too. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
