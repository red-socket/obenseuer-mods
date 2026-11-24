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

Screenshots are harness captures (headless, 2560×1440, UI scale 1.0) taken on 2026-11-24 with the installed release DLL (sha256 `b8bce6c7...`, not a test build), kept outside git in `~/Pictures/obenseuer-nexus/RecipeBook/`. The shot list planned the user's own system screenshots; these harness captures replace them. No shot shows the console, harness text or a debug overlay. Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below. Every gallery shot is cropped to 16:9, so the gallery frame's 1680×945 box shows it without further cropping. Alternates (not rendered) are in `alt/`: `rb-key-alt1.png` (closer), `rb-key-alt2.png` (stove smaller, more open space on the left), `rb-usedin-with-fieldguide-full.png` (the same tooltip with Field Guide's "Bought by: ..." line) and `rb-usedin-itempanel-full.png` (the item panel's "Used in 91 recipes").

**Status: rendered 2026-11-24** (`nexus/`: header, thumbnail, `gallery-01` to `gallery-04`; every caption fit at full size and all four are unchanged from the shot list, since each is literally true of its shot). A test result that changes the listing can change a caption too.

Tagline: "Search recipes and see the odds before you craft." The listing's "Find the recipe you want, see the odds before you craft, and know what's left to learn." is too long for the header, so the images shorten it. Key shot (header and thumbnail): `rb-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): the workbench or a stove with a few ingredients beside it, in the right third of the frame. Used: the public Barrel stove at the Roinaa warm-up place (see below).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `rb-search.png` | Open a crafting station (shot at the public Barrel stove, not the workbench) and type `lettuce` in the search bar above the recipe list. | Search by recipe or ingredient. |
| 02 | `rb-badges.png` | Open a stove (shot at the public Barrel stove, not an `add_furniture` stove): recipe buttons with "Fail n%" and "Lv 1/x" badges. | Each recipe shows its fail chance and the level needed. |
| 03 | `rb-usedin.png` | `add_item 22050 1` (Lettuce), hover it in the inventory: the "Used in:" line. Park Field Guide first: it adds a "Bought by: ..." line to the same tooltip. | Item tooltips list the recipes that use the item. |
| 04 | `rb-remaining.png` | `add_item 15100 1` (Open Sewer Magazine), select it in the inventory: the item panel's short line "New recipes: 198/198" (the tooltip's long form is "New recipes left: ..."). | Magazines show how many new recipes they still hold. |

| File | Derived from |
|---|---|
| `header.jpg`, `thumbnail.jpg` | `rb-key.png`, harness capture 2026-11-24, uncropped; `nohud` mode (the game's own Hide HUD, as P), no windows, no crosshair. The public Barrel stove at the Roinaa warm-up place in the right third, a cabbage on top, a carrot and a lettuce on the woodpile, a tomato and a potato on the pallet, all dropped with the inventory's Drop button; two NPCs warming up at the stove. `change_time 12:30` (the sky still reads blue, with street lights on). Camera at 15.4/-99.6/15.0, yaw about 35, pitch -12 |
| `gallery-01.jpg` | `rb-search.png`, harness capture 2026-11-24, cropped `720x405+1160+456` (the search bar, the Show button and the first four results); original `rb-search-full.png`. Barrel stove, `lettuce` typed and Enter pressed: "All · 169", then Glowing Hamburger, Hamburger, Glowing Sawdust Hamburger and Sawdust Hamburger. No result name contains "lettuce", so the shot shows the ingredient match |
| `gallery-02.jpg` | `rb-badges.png`, harness capture 2026-11-24, cropped `720x405+1160+456` (same crop as 01); original `rb-badges-full.png`. The same list unfiltered ("All · 299"): Cabbage Soup, Glowing Cabbage Soup, Turnip Soup and Sausage soup, each with "Fail 100%" over "Lv 1/x"; Glowing Cabbage Soup keeps a clear gap before its badge |
| `gallery-03.jpg` | `rb-usedin.png`, harness capture 2026-11-24, cropped `1280x720+540+532` (the Lettuce slot and the whole tooltip box); original `rb-usedin-full.png`. A real hover tooltip on Lettuce in the inventory, ending "Used in: Beef Multigrain Sandwich, ... Beef Sawdust Wheat Sandwich (+86 more)". Field Guide parked for this shot. The harness can't move the mouse, so a scratchpad copy of the test helper set a virtual mouse position; the game's own input system fired the hover and placed the tooltip (not the item-panel fallback) |
| `gallery-04.jpg` | `rb-remaining.png`, harness capture 2026-11-24, cropped `606x341+576+956` (just inside the item panel, no grey strips at the edges; upscaled about 2.8× in the frame, soft but readable); original `rb-remaining-full.png`. Open Sewer Magazine selected in the inventory: "New recipes: 198/198" |
