# Display Options art

No art in 0.1.0. Everything the mod shows in game is a game asset used at runtime or text inside the game's own UI.

| Use | Game asset | Treatment |
|---|---|---|
| "UI Scale" row in Pause > Options > Quality | runtime copy of the game's FOV row (`OptionsMenu.fovSlider`'s row, `GameObject/Pause Menu.prefab`: `Menu/Display/Fov Option`) | Same frame, slider, handle, font and sounds. Label text replaced, number text shows 75-150. The copy's Relays don't save, its localisation is removed and its slider event is new (it never calls `setFOV`). |

## Nexus images

Render with `mods/DisplayOptions/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080, with the tagline under the title) and one `gallery-NN.jpg` per shot, in the order below. Template elements come from the kit: `art/STYLE.md` and `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are harness captures (headless, 2560×1440) from the Harness_Test character after a fresh `test-save` reset, 2026-11-24, kept outside git in `~/Pictures/obenseuer-nexus/DisplayOptions/`. Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below. No crops: the menu text reads at full size, and the before/after pair needs the whole frame to compare.

**Status: rendered 2026-11-24.** All four shots are in; every caption still matches its shot, so none changed. A test result that changes the listing can change a caption too.

Tagline: "A steadier frame rate and more display settings." The listing's "A steadier frame rate, fewer shadows if you want them, camera effects you can turn off, and a bigger or smaller UI." is too long for the header, so the images shorten it. Key shot (header and thumbnail): `do-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): a busy, well-lit place (the Mallment or the Bazaar by day), with the most detail in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `do-slider.png` | Game closed: in `redsocket.obenseuer.displayoptions.cfg`, `[UiScale]`, set `InMenuSlider = true` (restore afterwards). Esc, Options, Quality: the UI Scale row. | Change the UI scale from the Options menu. |
| 02 | `do-ui-before.png` | The inventory open at UI Scale 100%. | Before: the inventory at the game's own size. |
| 03 | `do-ui-after.png` | Same view at UI Scale 110%. | After: the same inventory at 110%. |
| 04 | `settings-card.svg` (ours) | Rendered by `render.sh`, not a screenshot: the `[FrameRate]`, `[Shadows]`, `[PostProcessing]` and `[UiScale]` settings with their defaults from `Cfg.cs`. | Frame cap, shadows and camera effects in the config. |

| File | Derived from |
|---|---|
| `settings-card.svg` | Ours, no game image: layout of Sound Sanity's `settings-card.svg`; fonts Erika Ormig and Perfect DOS VGA 437 from `AssetRipper/ExportedAssets/Assets/Font`, grain `art/textures/concrete.png`; setting names and defaults from `Cfg.cs`. Left out on purpose to keep the card to four short sections: the `Custom*` shadow values, `ScalePauseMenu` and `VerboseLog` (the listing's Settings section has them all) |
| `header.jpg`, `thumbnail.jpg` | `do-key.png`, harness capture 2026-11-24; Open Sewer Bazaar plaza at noon, clear weather, HUD hidden as P does it, no windows: Hotel Korso and the café umbrellas left, the Casino "Tough Luck" front in the middle, a passer-by, bench, bank and a street NPC in the right third. Camera (44, -99.85, -41), yaw 180, pitch 9; the busiest of 24 candidate views |
| `gallery-01.jpg` | `do-slider.png`, harness capture 2026-11-24; `InMenuSlider = true` set in the config with the game closed (restored afterwards), Esc > Options > Quality, the UI Scale row at 100 above Apply/Back. The resting cursor at the screen centre put the game's red hover highlight on the SSAO value row (the harness can't move the cursor), so that row's red was removed by hand: inside the menu panel only (x 1035-1540, y 698-794 at 2560x1440) the red channel is clamped to max(green, blue) (`magick -region 505x96+1035+698 -channel R -fx "min(r,max(g,b))"`). The capture is kept as `do-slider-orig.png` |
| `gallery-02.jpg` | `do-ui-before.png`, harness capture 2026-11-24; inventory open at UI Scale 100% in the Mallment corridor, camera (2.5, -95.99, -57.5), yaw 300, pitch -3, the test save's own 20 items |
| `gallery-03.jpg` | `do-ui-after.png`, harness capture 2026-11-24; same position, camera and items at 110%, set in memory only with the DOTestHelper `dot_cfg` command (`.internal/test-helpers/DOTestHelper/`) and set back to 1 afterwards. The menu grows right and up and its tab row reaches the top edge: that is how the game lays it out on a smaller canvas (its panels hang from the top left, design §7), and 110% is the 16:9 limit chosen so it still fits (976 units). Not a capture fault |
| all | house-style kit templates (`art/templates/`), fonts and textures listed in `art/SOURCES.md` |
