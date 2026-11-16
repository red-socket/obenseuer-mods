# Fewer Clicks art

## Nexus images

Render with `mods/FewerClicks/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-15. The tagline shortens the listing's "Fewer clicks for the things you do a hundred times a day." so it fits the header at full size.

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/FewerClicks/` (copied from `~/Pictures/Screenshots/`). The test IDs refer to `.internal/test-plan.md`.

**Status: rendered 2026-11-16.** All shots are in.

Tagline: "Fewer clicks for the things you do all day." Key shot (header and thumbnail): `fc-key.png`, a gameplay screenshot with the HUD hidden (no UI windows).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `fc-topup-before.png` (optional) | Chest open, its Junk Wood stack at 4, 6 more in the inventory | Before: the chest holds 4 Junk Wood, and you carry 6. |
| 02 | `fc-topup.png` | Same chest right after Top up: the stack full, 2 left in the inventory | After Top up: the chest stack is full, and 2 stay with you. |
| 03 | `fc-swing.png` | Axe mid-swing on Tree Debris while holding the button | Hold the button on a tree or rock and the tool keeps swinging. |
| 04 | `fc-quickeat.png` | The notification after pressing 5, in normal play | Press 5 to eat and drink, and a notification says what you had. |
| 05 | `fc-fillall.png` | Fill all mid-run at a home sink: the first bottle stack filled, the second still empty | One click fills every container you carry, one after another. |

| File | Derived from |
|---|---|
| `header.jpg`, `thumbnail.jpg` | `fc-key.png`: `Screenshot From 2026-11-16 03-28-12.png` (pickaxe at a rock face in the mines, HUD hidden) brightened with `magick in.png -level 0%,50%,1.15 fc-key.png`, because the mine is too dark once the template darkens it |
| `gallery-01.jpg` | `fc-topup-before.png`, copy of `Screenshot From 2026-11-15 21-43-07.png` |
| `gallery-02.jpg` | `fc-topup.png`, copy of `Screenshot From 2026-11-15 21-43-17.png` |
| `gallery-03.jpg` | `fc-swing.png`, copy of `Screenshot From 2026-11-15 21-47-23.png` |
| `gallery-04.jpg` | `fc-quickeat.png`, copy of `Screenshot From 2026-11-16 03-31-53.png` |
| `gallery-05.jpg` | `fc-fillall.png`, copy of `Screenshot From 2026-11-16 03-30-29.png` |
| all | house-style kit templates (`art/templates/`), fonts and textures listed in `art/SOURCES.md` |

## In-game UI

The mod draws no art of its own. Both buttons are runtime clones of the game's buttons, unmodified apart from the label, size and a hidden controller glyph:

| Button | Cloned from | AssetRipper reference |
|---|---|---|
| Top up (`FC_TopUpButton`) | `StoragePanelUI.takeAllButton` ("Take all", 110x36, under the storage panel's `Buttons` row) | any scene, e.g. `AssetRipper/ExportedProject/Assets/Scenes/Interior Bazaar J Kiosk.unity`, GameObject "Take all" (fileID 5179); scripts `DefaultUIButton` and `Relay` (the Relay is removed from the clone) |
| Fill all (`FC_FillAllButton`) | `LiquidStoragePanelUI.FillButtonGO` ("Fill Button", 180x40 in its row; the clone is 110x36 in the title row) | same scene, GameObject "Fill Button" (fileID 6570) under the liquid panel's `Buttons`; parented to "Panel Title" (fileID 4215) |

Slot key labels use the game's own slot hotkey badge (`Slot.SetHotKeyNumber`), as on the hand and backpack slots.
