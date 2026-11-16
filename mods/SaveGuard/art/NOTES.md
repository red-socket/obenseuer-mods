# Save Guard art

## Nexus images

Render with `mods/SaveGuard/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-15.

Screenshots are the user's own captures from a test character at 1920×1080, UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/SaveGuard/`. The test IDs refer to `.internal/test-plan.md`.

**Status: waiting for screenshots.** `nexus/` still holds the first placeholder render (loading-screen background, placeholder caption) until `render.sh` replaces it.

Tagline: "Backups and crash-safe saves." Key shot (header and thumbnail): `sg-key.png`, a gameplay screenshot with the HUD hidden (no UI windows).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `sg-window.png` | Main menu, F6, a character with several backups, one restore point selected | Press F6 in the main menu to see every backup of a character. |
| 02 | `sg-confirm.png` | Same window, the "Replace Slot N ...?" confirmation | Save Guard keeps the slot you replace, so you can undo a restore. |
| 03 | `sg-damaged.png` (optional) | Throwaway slot with `Globals.tnmt` renamed, loaded from the main menu | Save Guard asks before you load a damaged save. |
| 04 | `sg-ingame.png` | In game, F6 status line | In game, F6 shows how many backups you have. |
| 05 | `sg-notice.png` (optional) | The pre-update or first-run notice in the main menu | Save Guard backs up every save before a new game version runs. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |

## In-game UI colours (`Ui/Skin.cs`)

The IMGUI windows use the house palette from `art/STYLE.md`, which is taken from the game's own UI prefabs:

| Use | Colour |
|---|---|
| Window background | `#1f1f1f` at 96 % opacity (charcoal) |
| List rows | `#2b2b2b` (panel), hover `#3a3a3a` |
| Selected row | `#835213` (brown, amber's partner for fills) |
| Text | `#cacaca`; secondary `#bcbab1`; small print `#8a8a88` (lighter than the divider grey so it stays readable at 14 px) |
| Accent, warnings, BROKEN/DAMAGED | `#ffab00` (amber). No red text on charcoal, per the style guide. |

Fonts are Unity's default IMGUI font; the game's own fonts are not loaded in v0.1.0.
