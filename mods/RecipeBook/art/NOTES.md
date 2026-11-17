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

**Status: not started.** Header, thumbnail and gallery follow the house kit (`art/STYLE.md`, `art/render.py` at the repo root) once the in-game tests have passed. The header will use game recipe item sprites; record each source path in the table below. Captions and the tagline go through the humanizer before rendering.

Tagline (draft): "Find recipes fast, see the odds, and know what's left to learn."

Suggested gallery shots (test IDs from `.internal/test-plan.md`):

| # | Screenshot | Set up | Caption (draft) |
|---|---|---|---|
| 01 | `rb-search.png` | T-ING: the Basic workbench filtered by "lettuce" | Search by recipe or ingredient. |
| 02 | `rb-badges.png` | T-BADGE: a stove list with fail and skill badges | See the fail chance and the level needed at a glance. |
| 03 | `rb-usedin.png` | T-USED: Lettuce's tooltip | Every item lists the recipes that use it. |
| 04 | `rb-remaining.png` | T-REMAIN: an Open Sewer magazine's item panel | Magazines tell you how many recipes are still new. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
