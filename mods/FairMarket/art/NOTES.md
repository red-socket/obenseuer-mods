# Fair Market art

Nothing in this folder ships: the mod draws no art of its own. Its in-game lines are plain text inside the game's own UI, and the Invoices window is IMGUI in the house palette.

| Use | Game asset | Treatment |
|---|---|---|
| Door and stall hours ("(Enter, open until 21:00)", "(Closed, opens 9:00)", the week line) | the game's use prompt (`UseText.useText`, TMP) | Inserted into vanilla's text; the red/green colour is the prompt's own. Week line at `<size=80%>`. ASCII hyphens. |
| Dishwasher odds ("Fail chance: 69.3% (base 99.0%, ...)", "Machine efficiency", "Process time") | the recipe details text (`RecipeInfoUI` details prefab, TMP) | Replaces vanilla's two lines in the same text object. |
| "Not washed:" note | a new TMP under the manufacturing panel's slot area | Font, material and colour copied from the panel title (`CraftingPanelBaseUI.title`), 60% size. Placement needs the screenshot in test D4. |
| Invoice tag "· scam" / "· no debt" | the item panel's value text (`ItemInfoPanel.itemValue`, TMP) | Appended in amber #ffab00 (the house accent). |
| Invoice tooltip lines | the game's tooltip (`ToolTip`) | One ASCII line under "Due in N days". |
| Invoices window | none (IMGUI) | House `Skin`: panel #1f1f1f, selected row #835213, amber #ffab00, copied from Quest Helper. |
| Pause menu entry "Invoices" (optional) | a clone of the pause menu's Resume button | Label only; the button's look is the game's. |

## Nexus images

**Status: not started.** Header, thumbnail and gallery follow the house kit (`art/STYLE.md`, `art/render.py` at the repo root) once the in-game tests have passed. Per the repo convention (commit 6f24696) the thumbnail shows the mod's tagline instead of MOD FOR OBENSEUER. Captions and the tagline go through the humanizer before rendering.

Tagline (draft, from the listing): "Know when shops open, what the dishwasher will do, and which invoices are real." Shorten it if it doesn't fit the header (for example "Shop hours, honest odds, real invoices").

Suggested gallery shots (test IDs from `.internal/test-plan.md`):

| # | Screenshot | Set up | Caption (draft) |
|---|---|---|---|
| 01 | `fm-hours.png` | H2: a locked Bazaar shop door at night with the week line | Shop doors show when they open. |
| 02 | `fm-dishwasher.png` | D1/D4: the dishwasher panel with the precise odds and the "Not washed" note | The dishwasher shows its real odds and what it skips. |
| 03 | `fm-invoices.png` | L3: the Invoices list with a real debt, a scam and the folded old letters | Every unpaid invoice in one list. |
| 04 | `fm-scam.png` | I1: a scam letter's item panel with "· scam" | Scam letters are marked. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
