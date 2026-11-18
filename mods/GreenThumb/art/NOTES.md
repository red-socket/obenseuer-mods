# Green Thumb art

Nothing in this folder ships: the mod draws no art of its own. Its in-game lines are plain text inside the game's own prompt (`UseText`, TMP), and the quick-plant bar and preview it hides are the game's own.

| Use | Game asset | Treatment |
|---|---|---|
| Hold hint "Hold - Keep harvesting, hold longer for everything in reach" | the game's use prompt (`UseText.UseAndHoldText`, the same colour tags) | Replaces vanilla's hold text only; ASCII. |
| Paused line "No power: growth paused", or "Switched off: growth paused" when the grower is switched Off | the plant's world tooltip title (`grow.GetTitle`) | One extra line under vanilla's "Growing …%" line; ASCII. |

## Nexus images

Render with `mods/GreenThumb/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080, with the tagline under the title) and one `gallery-NN.jpg` per shot, in the order below. Template elements come from the kit: `art/STYLE.md` and `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/GreenThumb/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. A test result that changes the listing can change a caption too.

Tagline: "Less clicking in the garden, and fewer grower bugs." The listing's "Less clicking in the garden, and the growers do what they say." is too long for the header (62 characters), so the images shorten it. Key shot (header and thumbnail): `gt-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): tomato plants with fruit in a Flowerbox, in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `gt-harvest.png` | `add_furniture Flowerbox 2`, `add_item 11210 6` (Potato Seeds), `add_item 11290 6` (Tomato Seeds). Plant both boxes, then `set_ingame_time_scale 60` until they're ripe (water them if needed) and `set_ingame_time_scale 1`. Hold E on the potatoes for over a second: a shot mid-sweep, with some plants already gone. | Keep holding Use to harvest everything in reach. |
| 02 | `gt-stacks.png` | Pick tomatoes a few in-game hours apart, then open the inventory: one tomato stack. | Produce of similar freshness stacks together. |
| 03 | `gt-percent.png` | Look at a tomato plant with a ripe fruit: "Growing fruits 100%". | The fruit percentage hits 100% when a fruit is ripe. |
| 04 | `gt-power.png` (optional) | `add_furniture Terrarium 1`, plant a seed, switch the flat's electricity off: "No power: growth paused". | Terrariums stop growing without power. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
