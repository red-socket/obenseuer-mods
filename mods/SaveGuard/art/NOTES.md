# Save Guard art

| File | What | Derived from |
|---|---|---|
| `nexus/header.jpg` | Nexus header, 1400×400 | Placeholder rendered with `uv run art/render.py "Save Guard" "Backups and crash-safe saves."` (no `--shot`), so the background is the game's loading-screen render. Sources of every template element: `art/SOURCES.md`. |
| `nexus/thumbnail.jpg` | Nexus thumbnail, 1920×1080 | Same render, same sources. |
| `nexus/gallery-01.jpg` | Gallery frame | Same render, same sources. |

To do before release: take in-game screenshots (the F6 window over the Load menu, the damaged-save prompt, the startup banner) and re-render with `--shot` so the header, thumbnail and gallery show the mod itself.

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
