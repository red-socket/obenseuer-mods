# Better Tenement art

## Nexus images

Render with `mods/BetterTenement/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-15.

Screenshots are 2560×1440 captures at UI scale 1.0: the user's own system screenshots from a test character, except `bt-card.png` and `bt-doctor.png`, which are game-harness captures (2026-11-19). All are kept outside git in `~/Pictures/obenseuer-nexus/BetterTenement/` (copied from `~/Pictures/Screenshots/`). The test IDs refer to `.internal/test-plan.md`.

**Status: rendered 2026-11-16; gallery-03 and gallery-05 re-rendered 2026-11-19 from new harness captures.** All required shots are in; the optional `bt-stolen` frame is skipped until someone captures it.

Tagline: "Landlording without the bookkeeping." Key shot (header and thumbnail): `bt-key.png`, a gameplay screenshot with the HUD hidden (no UI windows).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `bt-chest-before.png` (optional) | Resource chest at 176 slots, Stash button next to Sort, renovation materials in the inventory | Resource chests hold 176 stacks, with a Stash button next to Sort. |
| 02 | `bt-chest.png` | Same chest right after Stash | Stash moves every renovation material you carry into the chest. |
| 03 | `bt-card.png` | Upgrade card with the Ledger line | Upgrade cards show the rent effect and who can do the work. |
| 04 | `bt-ledger.png` | Tenement tab (H) with the Ledger panel (F3) | The Landlord Ledger shows rent, occupancy and running renovations. |
| 05 | `bt-doctor.png` | F4 in an uncleared flat, an amber outline through a wall | The Doctor shows what keeps a flat from counting as cleared. |
| 06 | `bt-stolen.png` (optional) | Test M1, the "Used 6 stolen first" notification | Contractors use your stolen materials first. |

| File | Derived from |
|---|---|
| `header.jpg`, `thumbnail.jpg` | `bt-key.png`, copy of `Screenshot From 2026-11-16 03-13-50.png`; the tenement block from the street at dusk, HUD hidden |
| `gallery-01.jpg` | `bt-chest-before.png`, copy of `Screenshot From 2026-11-15 21-48-26.png` |
| `gallery-02.jpg` | `bt-chest.png`, copy of `Screenshot From 2026-11-15 21-48-34.png` |
| `gallery-03.jpg` | `bt-card.png`, a headless harness capture from 2026-11-19 (`shot --mode full`, 2560×1440, HUD visible); Samuel Jonasson's contractor menu in Interior Tenement Deekula A, Tenement tab on the Basement panel, Makeshift and Shabby starred, Ledger collapsed to its button, CardLineOffset `20,-80`; material counts read 0/n (the test character carries no materials). The 2026-11-15 capture is kept as `bt-card-old.png` |
| `gallery-04.jpg` | `bt-ledger.png`, copy of `Screenshot From 2026-11-15 21-48-53.png` |
| `gallery-05.jpg` | `bt-doctor.png`, a headless harness capture from 2026-11-19 (`shot --mode full`, 2560×1440, HUD visible); the Doctor (F4) on Apartment 3 in Interior Player Tenement after examining it, leftovers listed with the first row selected and amber outlines through the wall. The 2026-11-16 capture is kept as `bt-doctor-old.png` |
| all | house-style kit templates (`art/templates/`), fonts and textures listed in `art/SOURCES.md` |

## In-game UI colours

- IMGUI windows (Ledger panel, Doctor window): the house palette from `art/STYLE.md`, through `Ui/Skin.cs` (copied from Save Guard): charcoal `#1f1f1f` at 96 %, rows `#2b2b2b`, selected row `#835213`, text `#cacaca`, small print `#8a8a88`, accent and warnings amber `#ffab00`.
- In the game's own TMP UI:
  - chest counter `#B8A27A` (a muted tan close to the panel's header text), amber `#FFAB00` above 88 stacks;
  - card count amber `#E0B050` (carried items will be used); red shortage stays the game's own;
  - Ledger deltas green `#9FD18B` / red `#E06060` (the card background is light, so red is readable there, unlike on charcoal).
- Separators "·" and "–" are in the game's Erika Ormig, Roboto and LiberationSans TMP fonts; "✓" is not, so the panels write "met".
- Doctor outlines default to `#FFAB00` (`Doctor.OutlineColor`).
