# Fewer Clicks art

## Nexus images

Render with `mods/FewerClicks/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-15. The tagline shortens the listing's "Fewer clicks for the things you do a hundred times a day." so it fits the header at full size.

Screenshots are the user's own captures from a test character at 1920×1080, UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/FewerClicks/`. The test IDs refer to `.internal/test-plan.md`.

**Status: waiting for screenshots.** `nexus/` is empty until `render.sh` runs.

Tagline: "Fewer clicks for the things you do all day." Key shot (header and thumbnail): `fc-swing.png`.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `fc-topup.png` | T-1 right after pressing Top up: header row with Top up, Sort and Take all, the chest's Junk Wood stack at 16/16, the 2 left over in the inventory | Top up fills the chest's existing stacks from your inventory. |
| 02 | `fc-swing.png` | A-1: axe mid-swing on Tree Debris while holding the button, HUD hidden, tree in the centre or right half | Hold the button on a tree or rock and the tool keeps swinging. |
| 03 | `fc-quickeat.png` | E-1: the notification listing what you ate and drank after pressing 5, in normal play | Press 5 to eat and drink, and a notification says what you had. |
| 04 | `fc-fillall.png` | F-1 mid-run at a home sink: the Fill all button reading "Stop", a bottle filling | One click fills every container you carry, one after another. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |

## In-game UI

The mod draws no art of its own. Both buttons are runtime clones of the game's buttons, unmodified apart from the label, size and a hidden controller glyph:

| Button | Cloned from | AssetRipper reference |
|---|---|---|
| Top up (`FC_TopUpButton`) | `StoragePanelUI.takeAllButton` ("Take all", 110x36, under the storage panel's `Buttons` row) | any scene, e.g. `AssetRipper/ExportedProject/Assets/Scenes/Interior Bazaar J Kiosk.unity`, GameObject "Take all" (fileID 5179); scripts `DefaultUIButton` and `Relay` (the Relay is removed from the clone) |
| Fill all (`FC_FillAllButton`) | `LiquidStoragePanelUI.FillButtonGO` ("Fill Button", 180x40 in its row; the clone is 110x36 in the title row) | same scene, GameObject "Fill Button" (fileID 6570) under the liquid panel's `Buttons`; parented to "Panel Title" (fileID 4215) |

Slot key labels use the game's own slot hotkey badge (`Slot.SetHotKeyNumber`), as on the hand and backpack slots.
