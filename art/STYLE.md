# red-socket house style (v1)

The look of every red-socket Obenseuer mod's Nexus images: header, thumbnail, gallery and description images. v1 is **"Native UI"**: the game's own menu palette (charcoal and amber), its typewriter menu font, and screenshots first. Mods should look like they belong in the game, not like an outsider's ad.

Everything here comes from the game's own files; [SOURCES.md](SOURCES.md) lists each one. The rationale and the rejected directions are in `.internal/art-style.md`.

## Quick start

```bash
uv run art/render.py "Better Hunger" "Hunger that makes sense." \
  --shot shots/eating.png "Eating restores hunger over time." \
  --shot shots/menu.png "Every value is tunable in the BepInEx config."
```

This writes `header.jpg`, `thumbnail.jpg` and `gallery-01.jpg`, `gallery-02.jpg`, ... to `mods/BetterHunger/art/nexus/`.

- Each `--shot` is a screenshot plus its caption and becomes one gallery frame. The first one also backs the header and thumbnail; `--key-shot IMAGE` picks a different one. With no `--shot`, the game's loading-screen render stands in.
- The script checks every output's pixel size and byte limit (see Nexus formats). It exits non-zero with a warning if a limit is broken or a text doesn't fit.
- `art/examples/ExampleMod/` holds a reference render; the author avatar is `brand/avatar.png`.
- Needs `uv`, `rsvg-convert` (librsvg), ImageMagick 7 (`magick`) and the AssetRipper export (for the fonts).

## Nexus formats

Nexus publishes few numbers, and the upload form redesigned in 2026 states none publicly. The table uses the official figures where they exist, and otherwise sizes measured on live mod pages on 2026-11-14. The full research, with every source URL and how each number was measured, is in `.internal/nexus-image-formats.md`.

| Asset | We export | Limit we enforce | Nexus facts behind it |
|---|---|---|---|
| **Header** (banner on the mod page) | **1400×400** JPG (3.5:1) | **≤ 400 KB** | Wiki "Creating a mod page" recommends 1400×400 ([wiki, 2022](https://wiki.nexusmods.com/index.php/Creating_a_mod_page)). The new form stores it at 1300×372. Shown `object-fit: cover`: 1300×372 at a 1920 px screen, 1240×372 at 1440, 970×269 at ≤1280. Nexus overlays the mod title and stats on the bottom ~150 px under a dark gradient. On mobile only the central ~57 % of the width shows, under a 70 % black overlay. The 400 KB limit is the last staff-stated one ([forum, 2020](https://forums.nexusmods.com/topic/8716508-banners-on-mod-pages/)). |
| **Thumbnail / primary image** | **1920×1080** JPG (16:9) | ≤ 2 MB | 16:9 is the official advice ([news, 2017](https://www.nexusmods.com/news/13277)). Tiles show it at ~336×189 from a 385 px-wide generated thumbnail. Other ratios get letterboxed, not cropped. The new form requires a thumbnail and at least one gallery image to publish. |
| **Gallery image** | **1920×1080** JPG (16:9) | ≤ 2 MB each | "16:9 ratio and full HD (1920x1080px)", at least 5 images ([wiki, 2022](https://wiki.nexusmods.com/index.php/Creating_a_mod_page)). Stored at upload size and served as WebP. The last stated limit was 2 MB (2015); the current form only warns. Before/after sliders exist since Aug 2026 ([news](https://www.nexusmods.com/news/15594)). |
| **Description image** (`[img]`) | **≤ 960 px wide**, at final size | ≤ 2 MB | Shown at natural size up to the column (~1300 / 1240 / 970 px). `[img width=…]` is reportedly ignored on the live page and small images get upscaled, so export at exactly the size to show. |
| **Avatar** | **200×200** PNG (`brand/avatar.png`) | ≤ 100 KB | Stored and shown round at 100×100. There is no custom profile banner; Nexus blurs the avatar for it. The 100 KB limit dates from 2015. |

Rules that apply to all of them: no adult content; AI-made promo art must be tagged "AI Media" (we don't use any, see Don't); upload JPG for screenshots and PNG for flat art, since Nexus re-encodes everything to WebP. Re-check the limits in the upload form the first time you upload, and update this table if they differ.

## Palette

All colours are the game's UI colours (counted from its prefabs, see SOURCES.md). Use no others.

| Role | Hex | Swatch use |
|---|---|---|
| Deep black | `#0e0e0e` | Keylines, shadows, text on amber |
| Charcoal (base) | `#1f1f1f` | Every background |
| Panel | `#2b2b2b` | Caption bars, raised boxes |
| Divider grey | `#636362` | Rules, disabled text, small print |
| Text | `#cacaca` | Titles, captions |
| Text, secondary | `#bcbab1` | Taglines, the wordmark |
| **Amber (accent)** | `#ffab00` | The one accent: title rule, labels, index badges, line art |
| Brown | `#835213` | Amber's partner for large fills (rare) |
| UI red | `#ab000d` | "Before"/"removed" labels only |
| UI green | `#28ab00` | "After"/"added" labels only |

Ratios: about 85 % charcoal and screenshot, 10 % text, ≤5 % amber. One amber element per area. Never set text in red on charcoal (contrast too low).

The red-socket logo keeps its own colours (`#d7263d`, `#a61b2e`, `#5e0e1c`, `#f46e7e`, `#17171b`). They appear only inside the logo, never as layout colours.

## Typography

Only two fonts, both shipped with the game (`AssetRipper/ExportedAssets/Assets/Font/`):

| Font | Role | Sizes (at 1920 px wide) | Notes |
|---|---|---|---|
| **Erika Ormig** | Mod name, taglines, captions, wordmark | Title 96–150 px; tagline 32–40 px; caption 26–38 px | The game's menu typewriter face. Monospaced (advance 0.6025 em), so a line's width is `0.6025 × size × characters`. Sentence case for names ("Better Hunger"), sentence with full stop for captions. |
| **Perfect DOS VGA 437** | Kickers and technical labels: version and compatibility lines, settings-card labels | 24–40 px | ALL CAPS, the game's terminal font. Code page 437: no `·`, `№` or curly quotes; use `/` and `-` as separators. In SVG, quote the name (`font-family="&apos;Perfect DOS VGA 437&apos;"`) because it ends in a number. |

Don't use faux bold, italics, letter-spacing, outlines or condensing. Titles get a soft drop shadow (`#0e0e0e`, 90 %, blur 10) only where they overlap a screenshot.

## Logo and wordmark: red-socket

- **Logo** (`brand/red-socket-logo.svg`): the official red-socket logo, a 16×16 pixel-art red socket face, copied unchanged from [red-socket/.github](https://github.com/red-socket/.github/blob/main/profile/red-socket.svg). `brand/red-socket-logo.png` is a 512 px render.
- **Pixel-exact sizes only**: draw it at a whole multiple of 16 px in the *exported* image (16, 32, 48, 64, ... px) and on whole-pixel coordinates, so every logo pixel stays a crisp square. The thumbnail template exports at 1.5×, so its 64 px logo is 42.667 grid units at (104, 610.667) = (156, 916) export pixels. Minimum 16 px. Clear space: a quarter of the logo's size on every side.
- **Wordmark** (`brand/red-socket-wordmark.svg`): the logo, then `red-socket` in Erika Ormig, lowercase, always hyphenated, in `#bcbab1`. Logo size = the multiple of 16 px nearest to 1.15 × the font size (Erika 26–28 → 32 px, Erika 56–60 → 64 px). Gap = about 0.35 × font size. Set the baseline so the cap height is centred on the logo.
- Placement: header top centre, thumbnail bottom-left, gallery caption bar right. Once per image, never larger than the mod name.
- Don't redraw, recolour, smooth, rotate or outline the logo, scale it by non-integer factors, or put it on amber or red.

## Layouts

All templates are SVG in `templates/`. Each has fixed pixel geometry; the numbers below are the source of truth. Fields `render.py` fills are marked with `data-field`; text with `data-fit-width` shrinks to fit (down to `data-min-size`) and the script warns if it still doesn't fit.

### Header: `templates/header.svg`, 1400×400 (3.5:1)

```
x: 0     300                      700                     1100    1400
   +------+------------------------+------------------------+------+  0
   | elev |   [logo 32] red-socket (Erika 26)               | elev |  30-62
   | line |          Example Mod  (Erika 96, fit 720, ≥52)  | line |  baseline 148
   | art  |               ━━━ amber 80×6                    | art  |  176
   |      |   Does one useful thing. (Erika 32, fit 720, ≥22)|      |  baseline 218
   |      +------------------ safe area ends ---------------+      |  240
   |      Nexus title, breadcrumb and stats sit here (dark gradient) |
   +--------------------------------------------------------------+  400
```

- Safe area: **x 300–1100, y 0–240**. Everything readable stays inside it. The side strips (tenement elevation line art) are decoration and may be cropped.
- Screenshot full-bleed, centre-cropped, under a charcoal scrim (radial, 94 % behind the text, 35 % at the edges), so busy screenshots stay quiet.
- Taglines: one sentence, about 40 characters at full size. The fit floor (22 px) allows up to ~54; `render.py` warns past that.
- No kicker line here: Nexus already shows the game and the mod name.


### Thumbnail / primary image: `templates/thumbnail.svg`, 1920×1080 (16:9), drawn on a 1280×720 grid

The SVG's `viewBox` is 1280×720 and it exports at 1.5×. The numbers below are grid units.

```
+------------------------------------------------------------+  0
|  64 safe                                     screenshot →  |
|  |█ (amber rule 12×268 at x=64, y=226)                     |
|  |█  Example Mod          ← Erika 150 (fit 1112 px, ≥72)   |  baseline 372
|  |█  Tagline.             ← Erika 40 amber (fit 1108, ≥28) |  baseline 460
|                                                            |
|  [logo] red-socket        ← logo 64 px + Erika 40          |  y 611–654
+------------------------------------------------------------+  720
   screenshot: x 384–1280 full height, slice; fade from charcoal (left 45 % at 90 %) to clear (95 %)
```

- Safe area: 64 grid units all round. Must stay readable at 336×189, the Nexus tile: check with `magick thumbnail.jpg -resize 336x small.png`.
- Mod names up to about 25 characters fit on one line at the 72 px minimum. Longer names: shorten (CLAUDE.md asks for short names anyway).
- Under the title goes the mod's tagline (the same line as the header), not a generic kicker: it says what the mod does on the Nexus tile. Up to about 46 characters at full size; longer ones shrink to the 28 px floor (about 66 characters). Changed from the `MOD FOR OBENSEUER` kicker on 2026-11-17 at the user's request.
- When the thumbnail's text and the header's middle band want different crops of the key shot, pass the header's crop with `render.py --header-shot` (Time Keeper does).

### Gallery frame: `templates/gallery.svg`, 1920×1080 (16:9)

```
+------------------------------------------------------------+  0
|   120 |  screenshot 1680×945 at (120,28), 1px #0e0e0e line  | 120
|       |                                                    |
|       +----------------------------------------------------+  973
|       |01| Caption in Erika 38 (fit 1300, ≥26) [32]red-socket|  bar 989–1053, amber 4px top edge
+------------------------------------------------------------+  1080
```

- The screenshot is never cropped if it is 16:9. Other aspect ratios are centre-cropped to fill the frame (`slice`); take screenshots at 16:9.
- One caption per image, one sentence, describing what the player sees ("Eating restores hunger over time."), not marketing.
- Raw, unframed screenshots are also fine in the gallery (Nexus shows them full screen). Use the frame for the first 3–5 images that explain the mod; dump extra raw shots after them.

## Screenshot treatment

- Capture at 1920×1080 (or 2560×1440 / 3840×2160; the scripts scale down), 16:9, in-game UI hidden unless the mod changes the UI.
- No colour grading, filters, sharpening, or dust on screenshots: the game's own desaturated look is the style. The only effects are the charcoal fade in header/thumbnail and the frame in gallery images.
- Show the mod's effect in the middle of the frame; header and thumbnail crop the screenshot's left and right edges.
- For before/after pairs use two gallery frames with captions starting `Before:` / `After:`. If you put them side by side, label them with small DOS-font tags in UI red (before) and UI green (after) on `#0e0e0e`.
- Never show cheats, the F1 console or debug overlays unless they are the documented feature.

## Images in descriptions (BBCode)

- Use `[img]https://...[/img]` with images hosted on Nexus (uploaded to the mod's gallery; right-click → copy image address) so they never go missing.
- Export description images at **960 px wide or less, at the exact size to show**: `[img width=]` is unreliable and small images get upscaled. For a framed screenshot: `magick gallery-01.jpg -resize 960x desc-01.jpg`.
- Since July 2026 you can also drag an image into the description editor and Nexus hosts it.
- Keep text in the BBCode, not baked into images, so it stays searchable and translatable.

## Do

- Let the screenshot carry the image; charcoal is the frame, not the subject.
- Keep one amber accent per area and one wordmark per image.
- Write captions as plain, specific sentences.
- Re-render all three formats with `render.py` after any change to the name or tagline, so they stay in sync.
- Record any new game asset you use in `SOURCES.md` before using it.

## Don't

- Use colours or fonts outside this guide, including system fonts.
- Use Loiste's logos, the OBENSEUER logo, or official key art in our images (we are not the developer; avoid any look of endorsement).
- Put text over busy screenshot areas without the fade or shadow.
- Use AI-generated art, stock photos or assets from other games.
- Ship raw game files in `art/`, in mod folders, or in Nexus archives.

## Versioning

This is house style **v1** (2026-11-14). When the style changes, bump the version here, note what changed in `.internal/art-style.md`, and re-render mods only when they get their next release.
