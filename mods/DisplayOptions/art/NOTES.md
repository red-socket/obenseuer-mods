# Display Options art

No art in 0.1.0. Everything the mod shows in game is a game asset used at runtime or text inside the game's own UI.

| Use | Game asset | Treatment |
|---|---|---|
| "UI Scale" row in Pause > Options > Quality | runtime copy of the game's FOV row (`OptionsMenu.fovSlider`'s row, `GameObject/Pause Menu.prefab`: `Menu/Display/Fov Option`) | Same frame, slider, handle, font and sounds. Label text replaced, number text shows 75-150. The copy's Relays don't save, its localisation is removed and its slider event is new (it never calls `setFOV`). |

## Nexus images

Render with `mods/DisplayOptions/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080, with the tagline under the title) and one `gallery-NN.jpg` per shot, in the order below. Template elements come from the kit: `art/STYLE.md` and `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/DisplayOptions/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. A test result that changes the listing can change a caption too.

Tagline: "A steadier frame rate and more display settings." The listing's "A steadier frame rate, fewer shadows if you want them, camera effects you can turn off, and a bigger or smaller UI." is too long for the header, so the images shorten it. Key shot (header and thumbnail): `do-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): a busy, well-lit place (the Mallment or the Bazaar by day), with the most detail in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `do-slider.png` | Game closed: in `redsocket.obenseuer.displayoptions.cfg`, `[UiScale]`, set `InMenuSlider = true` (restore afterwards). Esc, Options, Quality: the UI Scale row. | Change the UI scale from the Options menu. |
| 02 | `do-ui-before.png` | The inventory open at UI Scale 100%. | Before: the inventory at the game's own size. |
| 03 | `do-ui-after.png` | Same view at UI Scale 110%. | After: the same inventory at 110%. |
| 04 | `settings-card.svg` (ours) | Rendered by `render.sh`, not a screenshot: the `[FrameRate]`, `[Shadows]`, `[PostProcessing]` and `[UiScale]` settings with their defaults from `Cfg.cs`. | Frame cap, shadows and camera effects in the config. |

| File | Derived from |
|---|---|
| `settings-card.svg` | Ours, no game image: layout of Sound Sanity's `settings-card.svg`; fonts Erika Ormig and Perfect DOS VGA 437 from `AssetRipper/ExportedAssets/Assets/Font`, grain `art/textures/concrete.png`; setting names and defaults from `Cfg.cs` |
| (screenshots: filled in after the final render) | |
