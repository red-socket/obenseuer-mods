# Fair Justice art

## Nexus images

Render with `mods/FairJustice/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-15.

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/FairJustice/` (copied from `~/Pictures/Screenshots/`). The test IDs refer to `.internal/test-plan.md`.

**Status: rendered 2026-11-16.** `fj-bag` and `fj-locker` are optional and skipped. The orange bag only appears when the cell locker is already partly full from an earlier arrest in the same cell (vanilla never empties it between arrests, `Prison.cs:146`, and picks the cell at random), so one arrest with a full inventory does not trigger it.

Tagline: "Crime still doesn't pay, but you only pay for it once." Key shot (header and thumbnail): `fj-key.png`, a gameplay screenshot with the HUD hidden (no UI windows).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `fj-panel.png` | Bazaar cell (S3), two charge rows (a Theft plus `add_crime` or a trespass), panel top right | A panel shows when you get out and what you are in for. |
| 02 | `fj-bags.png` | Inventory and worn rucksack open, a stolen stack moved or hovered, no Theft notification | Moving stolen items between your own bags is no longer theft. |
| 03 | `fj-bag.png` (optional) | Test C2, the orange bag at the Bazaar overflow spot, notification if possible | What doesn't fit the cell locker goes into the orange bag. |
| 04 | `fj-locker.png` | Test K2, the cell locker with a permitted stack hovered ("Owned - <name>") | Items you were allowed to have stay in your locker. |

| File | Derived from |
|---|---|
| `header.jpg`, `thumbnail.jpg` | `fj-key.png`, copy of `Screenshot From 2026-11-16 03-15-07.png`; Bazaar jail cell, the bunks and the cell door, HUD hidden |
| `gallery-01.jpg` | `fj-panel.png`, copy of `Screenshot From 2026-11-16 03-35-46.png`; Bazaar cell, Other 29 min and Theft 4 min |
| `gallery-02.jpg` | `fj-bags.png`, copy of `Screenshot From 2026-11-15 21-53-17.png`; the meatball plate being moved is stolen (confirmed by the user 2026-11-16), so the caption holds |
| all | house-style kit templates (`art/templates/`), fonts and textures listed in `art/SOURCES.md` |

## In-game UI colours

- Sentence panel (IMGUI): the house palette from `art/STYLE.md`, through `Ui/Skin.cs` (copied from Save Guard and Better Tenement): charcoal `#1f1f1f` at 96 %, title amber `#ffab00`, text `#cacaca`, the place name in small print `#8a8a88`.
- Notifications use the game's own notification panel; Fair Justice draws nothing else in the game's UI.
- The "·" separator is in Unity's built-in IMGUI font.
