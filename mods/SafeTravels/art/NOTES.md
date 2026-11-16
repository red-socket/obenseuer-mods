# Safe Travels art

Nothing in this folder ships: the mod draws no art of its own. The pause menu entries are runtime copies of the game's own "Resume" button (`Pause Menu/.../Main/Resume` in `AssetRipper/ExportedProject/Assets/GameObject/Pause Menu.prefab`) with a new label. The green "Paid - walk through" prompt is the game's own use text (`UseText.ChangeUseText` with its green colour).

**Status: waiting for screenshots and the logo.** Render with the house kit (`art/SOURCES.md` at the repo root) once the shots exist; captions go through the humanizer skill first.

## Logo

Planned derivation (record the exact exported files here once made):

| Element | Derived from |
|---|---|
| Toll coin slot | `coin_slot_001` under `_Environment/__Static/_Tenement_Toll_Bridge/Buy` in `AssetRipper/ExportedProject/Assets/Scenes/Open Sewer Tenement.unity` (its mesh and material) |
| Minitrain | the minitrain locomotive prefab and its textures under `AssetRipper/ExportedProject/Assets` (search for the `Minitrain` component) |
| Pause menu style | the `Pause Menu.prefab` button font and colours (`Text (TMP)` under `Main/Resume`) |

## Nexus images

Screenshots are the user's own captures from a test character at 1920×1080, UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/SafeTravels/`. Test IDs refer to `.internal/test-plan.md`.

| # | Screenshot | Set up | Caption (draft) |
|---|---|---|---|
| 01 | `st-toll-paid.png` | T1: aiming at the toll coin slot right after paying, the green "Paid - walk through" prompt visible | Pay once, walk through. |
| 02 | `st-unstuck-menu.png` | U1: pause menu open with "Get Unstuck" between Difficulty and Quit to Menu | A way out when you're stuck. |
| 03 | `st-minitrain.png` | D1: a locomotive with three wagons on the Mines line, HUD hidden | Your wagons stay coupled. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
