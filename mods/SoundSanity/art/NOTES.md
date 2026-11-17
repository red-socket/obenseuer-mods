# Sound Sanity art

No art is shipped and none is drawn in game: the mod has no UI, only BepInEx config entries.

## Nexus images

Render with `mods/SoundSanity/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline (the listing's own) went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/SoundSanity/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. The in-game tests in `.internal/test-plan.md` have not run yet; a test result that changes the listing can change a caption too.

Tagline: "When you turn it down, it goes down." Key shot (header and thumbnail): `ss-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): a loud machine (the Small Jaw Crusher from 02, another angle) in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `ss-sliders.png` | Esc, Options: the volume sliders. | The volume sliders now reach sounds that ignored them. |
| 02 | `ss-machines.png` | `add_furniture Small Jaw Crusher 1`, `add_furniture Table Saw 1`, placed side by side in your flat; HUD hidden. | Crushers and saws can be turned down on their own. |
| 03 | `ss-radio.png` | `add_furniture Cheap Radio 1`, placed and switched on, aiming at it. | Radio music follows the Music slider and can be muted. |
| 04 | `settings-card.svg` (ours) | Rendered by `render.sh`, not a screenshot: the `[Machines]`, `[Radio]` and `[Routing]` settings with their defaults from `Cfg.cs`. | Each machine group has its own volume, from 0 to 100%. |

| File | Derived from |
|---|---|
| `settings-card.svg` | Ours, no game image: layout of Unofficial Patch's `fixes-card.svg`; fonts Erika Ormig and Perfect DOS VGA 437 from `AssetRipper/ExportedAssets/Assets/Font`, grain `art/textures/concrete.png`; setting names and defaults from `Cfg.cs` and NEXUS.md |
| (screenshots: filled in after the final render) | |
