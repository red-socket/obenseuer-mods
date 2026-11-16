# Better Tenement art

## Nexus images

Render with `mods/BetterTenement/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-15.

Screenshots are the user's own captures from a test character at 1920×1080, UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/BetterTenement/`. The test IDs refer to `.internal/test-plan.md`.

**Status: waiting for screenshots.** `nexus/` is empty until `render.sh` runs.

Tagline: "Landlording without the bookkeeping." Key shot (header and thumbnail): `bt-doctor.png`.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `bt-chest.png` | Resource chest at 176 slots, 90-100 stacks, scrolled part way, Stash button and counter visible | Resource chests hold 176 stacks and get a Stash button. |
| 02 | `bt-card.png` | Upgrade card with the Ledger line, ideally a "~" or amber count | Upgrade cards show what each tier adds to the rent. |
| 03 | `bt-ledger.png` | Tenement tab (H) with the Ledger panel (F3) and a shopping list for starred upgrades | The Ledger shows rent, renovations and a shopping list. |
| 04 | `bt-doctor.png` | F4 in an uncleared flat, an amber outline through a wall | The Doctor shows what keeps a flat from counting as cleared. |
| 05 | `bt-stolen.png` | Test M1, the "Used 6 stolen first" notification | Contractors use your stolen materials first. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |

## In-game UI colours

- IMGUI windows (Ledger panel, Doctor window): the house palette from `art/STYLE.md`, through `Ui/Skin.cs` (copied from Save Guard): charcoal `#1f1f1f` at 96 %, rows `#2b2b2b`, selected row `#835213`, text `#cacaca`, small print `#8a8a88`, accent and warnings amber `#ffab00`.
- In the game's own TMP UI:
  - chest counter `#B8A27A` (a muted tan close to the panel's header text), amber `#FFAB00` above 88 stacks;
  - card count amber `#E0B050` (carried items will be used); red shortage stays the game's own;
  - Ledger deltas green `#9FD18B` / red `#E06060` (the card background is light, so red is readable there, unlike on charcoal).
- Separators "·" and "–" are in the game's Erika Ormig, Roboto and LiberationSans TMP fonts; "✓" is not, so the panels write "met".
- Doctor outlines default to `#FFAB00` (`Doctor.OutlineColor`).
