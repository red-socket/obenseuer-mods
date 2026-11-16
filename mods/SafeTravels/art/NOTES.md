# Safe Travels art

Nothing in this folder ships: the mod draws no art of its own. The pause menu entries are runtime copies of the game's own "Resume" button (`Pause Menu/.../Main/Resume` in `AssetRipper/ExportedProject/Assets/GameObject/Pause Menu.prefab`) with a new label. The green "Paid - walk through" prompt is the game's own use text (`UseText.ChangeUseText` with its green colour).

Nexus images: see below. The logo is separate work and not started.

## Logo

Planned derivation (record the exact exported files here once made):

| Element | Derived from |
|---|---|
| Toll coin slot | `coin_slot_001` under `_Environment/__Static/_Tenement_Toll_Bridge/Buy` in `AssetRipper/ExportedProject/Assets/Scenes/Open Sewer Tenement.unity` (its mesh and material) |
| Minitrain | the minitrain locomotive prefab and its textures under `AssetRipper/ExportedProject/Assets` (search for the `Minitrain` component) |
| Pause menu style | the `Pause Menu.prefab` button font and colours (`Text (TMP)` under `Main/Resume`) |

## Nexus images

Render with `mods/SafeTravels/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-16; all of them fit at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/SafeTravels/` (copied from `~/Pictures/Screenshots/`; the uncropped originals of cropped shots are kept there as `*-full.png`). Test IDs refer to `.internal/test-plan.md`.

**Status: rendered and approved by the user 2026-11-16.** All required shots are in; the optional `st-rerail` frame waits on test L0.

Tagline: "Fewer ways to get stuck, robbed or left behind." The tagline shortens the listing's "Fewer ways to get stuck, robbed by a coin slot or left behind by your own wagons." so it fits the header. Key shot (header and thumbnail): `st-key.png`, a gameplay screenshot with the HUD hidden (no UI windows).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `st-toll-paid.png` | T1: at the Tenement toll bridge, aiming at the coin slot right after paying, the green "Paid - walk through" prompt visible | Once you've paid, the toll slot takes no more coins. |
| 02 | `st-unstuck-menu.png` | U1: pause menu open with "Get Unstuck" between Difficulty and Quit to Menu | Get Unstuck in the pause menu takes you back a short way. |
| 03 | `st-minitrain.png` | D1: a locomotive with wagons coupled behind it on the Mines line, HUD hidden | Your wagons stay coupled through switches and curves. |
| 04 | `st-rerail.png` (optional) | L1: a derailed locomotive back on its rails after End; only if test L0 passes and Rerail ships on | Press End to put a derailed locomotive back on its rails. |

| File | Derived from |
|---|---|
| `header.jpg`, `thumbnail.jpg` | `st-key.png`, copy of `Screenshot From 2026-11-16 05-10-01.png`; the toll booth and the closed gate at the Tenement toll bridge, HUD hidden (the wider shot from the bridge, `Screenshot From 2026-11-16 05-09-39.png`, is kept as `st-key-a.png`) |
| `gallery-01.jpg` | `st-toll-paid.png`, copy of `Screenshot From 2026-11-16 05-11-05.png`; the coin slot after paying, green "(Paid - walk through)" prompt |
| `gallery-02.jpg` | `st-unstuck-menu.png`: `Screenshot From 2026-11-16 05-11-21.png` cropped to the pause menu with `magick in.png -crop 1280x720+640+360` so the entries are readable |
| `gallery-03.jpg` | `st-minitrain.png`, copy of `Screenshot From 2026-11-16 05-20-56.png`; a locomotive and a wagon coupled in the Sewer Mines |
| all | house-style kit templates (`art/templates/`), fonts and textures listed in `art/SOURCES.md` |
