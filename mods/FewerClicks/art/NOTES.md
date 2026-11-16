# Fewer Clicks art

## Nexus images

**Status: not started.** No logo, header or screenshots yet. When they are made, follow the kit and render setup the other mods use (`art/SOURCES.md` at the repo root, a `render.sh` in this folder writing to `nexus/`), keep the user's screenshots outside git in `~/Pictures/obenseuer-nexus/FewerClicks/`, and run captions and the tagline through the humanizer skill.

Suggested shots (test IDs from `.internal/test-plan.md`):

| # | Screenshot | Set up |
|---|---|---|
| 01 | `fc-topup.png` | T-1 after pressing Top up: header row Top up, Sort, Take all; the chest stack at 16 |
| 02 | `fc-fillall.png` | F-1 mid-run at a home sink: Fill all button reading "Stop", a bottle filling |
| 03 | `fc-quickeat.png` | E-1 notification "Ate Bread, drank Water" in normal play |
| 04 | `fc-swing.png` | A-1 axe mid-swing on a tree (HUD hidden) |

| File | Derived from |
|---|---|
| (filled in after the first render) | |

## In-game UI

The mod draws no art of its own. Both buttons are runtime clones of the game's buttons, unmodified apart from the label, size and a hidden controller glyph:

| Button | Cloned from | AssetRipper reference |
|---|---|---|
| Top up (`FC_TopUpButton`) | `StoragePanelUI.takeAllButton` ("Take all", 110x36, under the storage panel's `Buttons` row) | any scene, e.g. `AssetRipper/ExportedProject/Assets/Scenes/Interior Bazaar J Kiosk.unity`, GameObject "Take all" (fileID 5179); scripts `DefaultUIButton` and `Relay` (the Relay is removed from the clone) |
| Fill all (`FC_FillAllButton`) | `LiquidStoragePanelUI.FillButtonGO` ("Fill Button", 180x40 in its row; the clone is 110x36 in the title row) | same scene, GameObject "Fill Button" (fileID 6570) under the liquid panel's `Buttons`; parented to "Panel Title" (fileID 4215) |

Slot key labels use the game's own slot hotkey badge (`Slot.SetHotKeyNumber`), as on the hand and backpack slots.
