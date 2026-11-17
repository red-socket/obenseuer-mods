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

**Status: rendered 2026-11-17, waiting for the user's approval.** The optional rug shot is skipped. The in-game tests in `.internal/test-plan.md` have not run yet; a test result that changes the listing can change a caption too.

Tagline: "Straight rows without squinting." Key shot (header and thumbnail): `bp-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): furniture lined up neatly, in the right third of the frame. Candidates taken 2026-11-17 (a reading corner: Fancy Wooden Square Table with a Table Lamp and Small Potted Plant, Floor Lamp, two armchairs, Wall Clock, Rug Persian): `bp-key-a.png` (05-44-28, table and lamp right of centre, armchair left), `bp-key-b.png` (05-44-43, wider, chesterfield armchair in the right third), `bp-key-c.png` (05-43-49, like a without the rug). Chosen: a, copied to `bp-key.png` uncropped (user, 2026-11-17).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `bp-row.png` | Two wall cupboards side by side, the second snapped flush to the first with Left Alt held: the amber edge line with the tick at the shared side. | Hold Alt and the piece snaps flush to its neighbours. |
| 02 | `bp-stack.png` | A small cardboard box set on a bigger one with Left Alt held: blue centre lines both ways. | Blue lines mark centres, so stacked boxes sit square. |
| 03 | `bp-wall.png` | Painting Fox (`add_furniture Painting Fox 1`) beside a wall cupboard with Left Alt held: the amber line along both top edges. | On a wall, a painting lines up level with a cabinet. |
| 04 | `bp-snap.png` | Placing the painting while rotating with Mouse 5 held: turned 45°, the controls line shows the step keys. | Hold Mouse 4 or Mouse 5 to turn in 15° or 45° steps. |
| 05 | `bp-rug.png` (optional) | Game closed: in `redsocket.obenseuer.betterplacement.cfg`, `[FlatItems]`, set `Enabled = true` (restore afterwards). `add_furniture Cottage Table 1`, `add_furniture Rug Persian 1`; place the table, then the rug under it. | Optional: rugs can go under tables and chairs. |

| File | Derived from |
|---|---|
| `bp-key.png` | Screenshot From 2026-11-17 05-44-28 (`bp-key-a.png`), uncropped |
| `bp-row.png` | Screenshot From 2026-11-17 05-49-12, cropped `1600x900+200+150`; original `bp-row-full.png`. Spares: `bp-row-hud-full.png` (05-48-14, same with the building menu), `bp-edge-full.png` (05-48-49, two cupboards apart, bottom edges lined up) |
| `bp-stack.png` | Screenshot From 2026-11-17 05-50-05, uncropped. Spares: `bp-floor-full.png` (05-49-36, two boxes on the floor, centre line), `bp-chairs-full.png` (05-50-44, armchair and chair, centre line) |
| `bp-wall.png` | Screenshot From 2026-11-17 05-53-07, cropped `1600x900+880+180`; original `bp-wall-full.png` |
| `bp-snap.png` | Screenshot From 2026-11-17 05-54-07, cropped `1422x800+570+470` so the controls line reads; original `bp-snap-full.png` |
