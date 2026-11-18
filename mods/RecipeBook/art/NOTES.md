# Recipe Book art

Nothing in this folder ships: version 0.1.0 draws no art of its own. Every visible part is built at runtime from the game's own UI pieces.

| Use | Game asset | Treatment |
|---|---|---|
| Search box and Show button | the recipe button prefab's sprite and font (`RecipeListUI.recipeButtonPrefab`: `GameObject/UI Button Normal Recipe.prefab`, Erika Ormig SDF Menu) | Sprite at 85% brightness behind the search box, as is behind the Show button. 14 pt text in the label colour. |
| Show button highlight (controller) | the same prefab's hover sprite (`DefaultUIButton.hoverImage`) | Drawn over the button while the pad is on the bar; an amber tint if the sprite can't be read. |
| Badges ("Fail 20%", "Lv 1/5") | a TMP child of each recipe button, the label's font and colour | 11 pt, right-aligned, amber #FFC94D for warnings. |
| "No recipes match" | a TMP under the list's viewport | Label font and colour, 14 pt. |
| "Used in" and "New recipes left" | the game's tooltip text and the item panel's description (`ItemInfoDescription`) | Plain lines added to the game's text; the tooltip's labels are bold like the game's "Required in" block. |

## Nexus images

Render with `mods/RecipeBook/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080, with the tagline under the title) and one `gallery-NN.jpg` per shot, in the order below. Template elements come from the kit: `art/STYLE.md` and `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/RecipeBook/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. A test result that changes the listing can change a caption too.

Tagline: "Search recipes and see the odds before you craft." The listing's "Find the recipe you want, see the odds before you craft, and know what's left to learn." is too long for the header, so the images shorten it. Key shot (header and thumbnail): `rb-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): the workbench or a stove with a few ingredients beside it, in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `rb-search.png` | Open the workbench in your flat and type `lettuce` in the search bar above the recipe list. | Search by recipe or ingredient. |
| 02 | `rb-badges.png` | `add_furniture Stove 1`, place it, open it: recipe buttons with "Fail n%" and "Lv 1/5" badges. | Each recipe shows its fail chance and the level needed. |
| 03 | `rb-usedin.png` | `add_item 22050 1` (Lettuce), hover it in the inventory: the "Used in:" line. | Item tooltips list the recipes that use the item. |
| 04 | `rb-remaining.png` | `add_item 15100 1` (Open Sewer Magazine), select it in the inventory: "New recipes left: ...". | Magazines show how many new recipes they still hold. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
