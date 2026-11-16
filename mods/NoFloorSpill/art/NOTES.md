# No Floor Spill art

## Nexus images

Render with `mods/NoFloorSpill/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-15.

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/NoFloorSpill/` (copied from `~/Pictures/Screenshots/`). The test IDs refer to `.internal/test-plan.md`.

**Status: rendered 2026-11-16.** Before and after are in; the optional `nfs-chest` and `nfs-leftover` frames are skipped until someone captures them.

Tagline: "Ash and leftovers stay off the floor." Key shot (header and thumbnail): `nfs-key.png`, a gameplay screenshot with the HUD hidden (no UI windows).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `nfs-before.png` | Same camera as nfs-after, `[Refuse] Mode = Vanilla`, ash spawned with S3 (N = 10-20) | Before: once the ash slot is full, ash piles up on the floor. |
| 02 | `nfs-after.png` | Default Delete mode, burning stove with a full ash slot, clean floor, HUD hidden | After: the extra ash is deleted, and the floor stays clean. |
| 03 | `nfs-chest.png` (optional) | `[Refuse] Mode = NearbyStorage`, an owned chest within 4 m, opened, holding the ash | Or the extra ash goes into a chest you own nearby. |
| 04 | `nfs-leftover.png` | A full machine running out of butterine or glue, window open, Plastic Trash in the inventory | Leftovers go into your inventory while the machine is open. |

| File | Derived from |
|---|---|
| `header.jpg`, `thumbnail.jpg` | `nfs-key.png`, copy of `Screenshot From 2026-11-16 03-17-05.png`; the lit barrel stove in the Tenement Greenhouse, HUD hidden |
| `gallery-01.jpg` | `nfs-before.png`, copy of `Screenshot From 2026-11-16 03-58-35.png`; Metal Stove in the player flat, `Mode = Vanilla`, ash spilled around it |
| `gallery-02.jpg` | `nfs-after.png`, copy of `Screenshot From 2026-11-16 03-59-25.png`; same stove and angle with the ash picked up by hand in the same Vanilla session (the user chose this over a separate Delete-mode run; it looks the same) |
| all | house-style kit templates (`art/templates/`), fonts and textures listed in `art/SOURCES.md` |

## In-game UI

- The mod draws no UI and shows no notifications. Its only feedback is the BepInEx log.
