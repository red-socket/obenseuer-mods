# Field Guide art

Nothing in this folder ships: the mod draws no art of its own. The pop-ups use the game's own hint panel (`HintOverlay`: title, bulleted body, the "Hide hints" key button), and the Bought-by line is text inside the game's own tooltip and item panel.

| Use | Game asset | Treatment |
|---|---|---|
| Station guides | the game's hint panel (`HintOverlay`, the same panel as the card-game rules) | Title, bullets and a plain closing line, built with `HintContent`. Bullets follow the rules hints' style (sentence case, no full stop). |
| "Bought by" (tooltip) | the item tooltip's details text (`TooltipUI.details`, TMP, rich text) | Bold header like the vanilla "Required in" block; names in `<noparse>`; " · " (U+00B7) between traders. |
| "Bought by" (item panel) | `ItemInfoPanel.itemDescripion` (TMP, rich text off) | Plain text under the description; dropped when it doesn't fit. |

## Nexus images

Render with `mods/FieldGuide/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080, with the tagline under the title) and one `gallery-NN.jpg` per shot, in the order below. Template elements come from the kit: `art/STYLE.md` and `art/SOURCES.md` at the repo root. The tagline and captions went through the humanizer skill on 2026-11-25; every caption is at most 56 characters and renders at full size.

Screenshots are harness captures (headless, 2560×1440, UI scale 1.0) taken on 2026-11-25 with the installed release DLL (sha256 `d104211b...`, not rebuilt, not a test build), kept outside git in `~/Pictures/obenseuer-nexus/FieldGuide/`. Save: Harness_Test Autosave2 (8 visited places), saved once to Slot8 as a base; scenes changed with `change_scene`. Recipe Book, Fair Market and Tidy Inventory were parked for the whole session, so no other mod's line is in the tooltip or panel. For the session only, `Hidden` in the Field Guide config was cleared and `player_stats clear_needs` removed the red low-health tint. The 878 blocked achievements come from the test character's state, not the shots. No shot shows the console, harness text or a debug overlay. Every gallery shot is cropped to 16:9 from its `*-full.png` original and was checked at a 640 px downscale. Alternates (not rendered): `fg-key-b.png`, `fg-key-c.png` (other framings of the same nook).

**Status: rendered 2026-11-25** (`nexus/`: header, thumbnail, `gallery-01` to `gallery-04`; tagline and every caption fit at full size; thumbnail checked at 336 px). All four planned shots were made. The planned controller shot (`fg-panel-pad.png`, "Works with a controller.") was dropped: there was no real controller test, so shot 04 is the same panel selected with the keyboard and its caption makes no controller claim. A test result that changes the listing can change a caption too.

Tagline: "How stations work, and who buys what." It shortens the listing's heading "How does this thing work, and who buys that?" to a statement that fits the header and says what the mod does on the Nexus tile. Key shot (header and thumbnail): `fg-key.png`, the moonshine nook on Pontikka floor 3 with the HUD hidden.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `fg-mash.png` | Open a Mashing barrel with ingredients, yeast and water in its storage. | Opening the mash tun shows a short guide to mashing. |
| 02 | `fg-sauna.png` | A built basement sauna, Barrel stove (fuel variant) panel open. | The sauna stove gets a guide to heat and steam. |
| 03 | `fg-tooltip.png` | Hover a Gold Ring after visiting the Bazaar. | Tooltips list who buys an item and what each pays. |
| 04 | `fg-panel.png` | Select the Gold Ring in the inventory (keyboard): the item panel. | The item panel in the inventory lists buyers too. |

| File | Derived from |
|---|---|
| `header.jpg`, `thumbnail.jpg` | `fg-key.png`, harness capture 2026-11-25, full frame 2560×1440, uncropped; `--mode clean` (no HUD, no crosshair). Pontikka floor 3, the Mashing_barrel nook: warm wall lamp, firewood, the barrel in the right third. Rejected: Pontikka floor 5, the Bazaar Sauna lobby (drinks machine), the smoke sauna (naked NPC), the basement sauna (too dark), the Pawn Shop (cramped) |
| `gallery-01.jpg` | `fg-mash.png`, harness capture 2026-11-25, cropped `1656x932+96+128`; original `fg-mash-full.png`. The mash tun open with the whole "Mashing" guide, the "Mashing tun" title, the switch and fuel controls and the first storage slot (Potato x4); yeast and a 0.5 L Clean Water bottle are in the storage outside the crop. All moved in with the keyboard (WASD, F "Move Item"). The crop cuts the empty bottom of the hint panel and its "K Hide hints" footer, so the text stays readable at 640 px; a block of the game's orange blurred background shows top left |
| `gallery-02.jpg` | `fg-sauna.png`, harness capture 2026-11-25, cropped `1600x900+50+128`; original `fg-sauna-full.png`. Basement Sauna lvl 1 "Barrel stove" (fuel variant) with the whole "Sauna" guide, the stove title and controls; the same cut of the hint panel's bottom and footer as 01 |
| `gallery-03.jpg` | `fg-tooltip.png`, harness capture 2026-11-25, cropped `760x428+1243+683` (the tooltip only); original `fg-tooltip-full.png`. Gold Ring tooltip: "Bought by: Rihkama & Trinkets (3168 OC) · Pawn Shop (1980 OC) · Keke's Hoard (1386 OC) (+6 in places you haven't been)". The tooltip follows the real mouse, which the harness can't move, so the cursor stayed at the screen centre; the ring was hovered with the stove open so nothing under the cursor got a hover bracket. The hovered slot is not in the image |
| `gallery-04.jpg` | `fg-panel.png`, harness capture 2026-11-25, cropped `650x366+556+948` (just inside the item panel's grey border; the capture's crop `670x377+536+944` showed a black strip on the left and was re-cropped at render time); original `fg-panel-full.png`. Gold Ring selected with the keyboard: "Bought by: Rihkama & Trinkets (3168 OC) · Pawn Shop (1980 OC) (+1 more)". Gold Ring because it was the only item checked with two named buyers and prices (Teddy Bear's line doesn't fit, Scrap Metal shows only "Bought by 5 traders") |
