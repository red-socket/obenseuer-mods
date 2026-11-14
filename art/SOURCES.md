# Art sources and provenance

Every game asset the house-style kit uses, where it comes from, and what we did to it. Paths are relative to the repository root; `AssetRipper/` is the local export of Obenseuer v0.5.19 (Steam build 26404221) and is never committed or shipped.

Rule of thumb: the kit ships **derived** files only (recoloured, re-levelled, resized). Raw game files are read at render time from `AssetRipper/` and never copied into `art/`.

## Files in `art/textures/` (derived, in git)

| Kit file | Game source | What was done |
|---|---|---|
| `textures/concrete.png` (512×512) | `AssetRipper/ExportedProject/Assets/Texture2D/concrete_002.png` | Greyscale, auto-levelled, levels 20–80 %. Tiled at 7 % opacity as surface grain. |
| `textures/tenement-elevation.png` (1024×830) | `AssetRipper/ExportedProject/Assets/Texture2D/blueprint_front.png` (the tenement front elevation from the renovation blueprint UI) | Line art recoloured to amber `#ffab00`, alpha kept, resized from 2048 to 1024 px wide. Used at 14–18 % opacity behind header text. |
| `textures/dust.png` (1280×720) | `AssetRipper/ExportedProject/Assets/Texture2D/LoadingScreenBg_effect.png` (the dust overlay of the game's loading screen) | Alpha boosted (levels 0–60 %), colour set to white, resized from 1920 to 1280 px. |

Commands used (ImageMagick 7, run from the repo root):

```bash
T=AssetRipper/ExportedProject/Assets/Texture2D
magick $T/concrete_002.png -colorspace gray -auto-level -level 20%,80% -strip art/textures/concrete.png
magick $T/blueprint_front.png -fill "#ffab00" -colorize 100 -resize 1024x -strip art/textures/tenement-elevation.png
magick $T/LoadingScreenBg_effect.png -alpha extract -level 0,60% \( +clone -fill white -colorize 100 \) +swap \
  -compose CopyOpacity -composite -resize 1280x -strip art/textures/dust.png
```

## Read at render time (not copied)

| Use | Game source | Notes |
|---|---|---|
| Display font (titles, captions, wordmark) | `AssetRipper/ExportedAssets/Assets/Font/Erika Ormig.ttf` | The game's menu and UI typewriter font (TextMeshPro assets `Erika Ormig SDF Menu` / `Erika Ormig SDF`, used in about 300 prefabs and scenes). |
| Label font (kickers, technical lines) | `AssetRipper/ExportedAssets/Assets/Font/PerfectDOSVGA437.ttf` | The game's terminal/DOS font. |
| Placeholder screenshot | `AssetRipper/ExportedProject/Assets/Texture2D/LoadingScreenBg_weird_people.png` | The loading-screen render (3840×2160). Used only when `render.py` gets no `--shot`, and in `examples/ExampleMod/` (scaled and composited, so derived). Replace it with real screenshots before any release. |

`render.py` points fontconfig at the font folder for the run and installs nothing. To edit the SVG templates in Inkscape, install those two fonts locally (for example, copy them to `~/.local/share/fonts/`); don't commit the font files.

## Colours

The palette in [STYLE.md](STYLE.md) is taken from the game's UI, not picked by eye. It is a tally of `m_Color` / `m_fontColor` values across every prefab and scene in `AssetRipper/ExportedProject/Assets` (Unity UI `Image` and TextMeshPro components):

| Hex | Occurrences | Where the game uses it |
|---|---|---|
| `#ffab00` | 12,864 | Amber highlight (selected/active UI) |
| `#835213` | 12,864 | Brown (the pair to amber, button bodies) |
| `#0e0e0e` | 9,664 | Deepest panel black |
| `#1f1f1f` | 8,862 | Main panel charcoal (also `main_UI_background_001.png`) |
| `#2b2b2b` | 5,663 | Raised panel |
| `#636362` | 6,765 | Disabled / divider grey |
| `#cacaca` | 35,617 (font) | Main UI text |
| `#bcbab1` | 4,674 (font) | Secondary UI text |
| `#ab000d` | 5,551 | UI red (warnings, negative values) |
| `#28ab00` | 5,551 | UI green (positive values) |

## Brand (not a game asset)

| Kit file | Source | Notes |
|---|---|---|
| `brand/red-socket-logo.svg` | The official red-socket logo, https://raw.githubusercontent.com/red-socket/.github/refs/heads/main/profile/red-socket.svg (fetched 2026-11-14) | Copied unchanged. Our own brand, so it is the one exception to "art from the game's assets". |
| `brand/red-socket-logo.png` | Render of the SVG above at 512×512 | `rsvg-convert -w 512 red-socket-logo.svg -o red-socket-logo.png` |
| `brand/red-socket-wordmark.svg` | The logo plus "red-socket" set in Erika Ormig | Layout only. |
| `brand/avatar.svg` / `avatar.png` | The logo at 8× (128 px) on charcoal `#1f1f1f`, 200×200 | `rsvg-convert avatar.svg -o avatar.png` |

## Rejected directions (reference only)

The rejected mock-ups (see `.internal/art-style.md`) also used `Decal_002_poster_way_up.png`, `Decal_002_open_sewer.png`, `Decal_002_stalburg_steel.png` (redrawn skyline and palette), `plasterwall_012.png` and `crumpled_paper.png`. None of them ship in the kit.
