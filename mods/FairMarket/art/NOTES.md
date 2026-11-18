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

Render with `mods/FairMarket/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080, with the tagline under the title) and one `gallery-NN.jpg` per shot, in the order below. Template elements come from the kit: `art/STYLE.md` and `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/FairMarket/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. A test result that changes the listing can change a caption too.

Tagline: "Opening hours, dishwasher odds and real invoices." The listing's "Know when shops open, what the dishwasher will do, and which invoices are real." is too long for the header, so the images shorten it. Key shot (header and thumbnail): `fm-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): a Bazaar shopfront at dusk (`change_scene Open Sewer Bazaar`, `change_time 20`), in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `fm-hours.png` | `change_scene Open Sewer Bazaar`, `change_time 22`, aim at a closed shop door: "(Closed, opens 9:00)". | Shop doors say when they open and close. |
| 02 | `fm-dishwasher.png` | `add_furniture Dishwasher 1`, `add_item 15350 1` (Dishwasher Powder), `add_item 12570 1` (Gold Ring), plus a small item stolen from a shop. Put the stolen item and the ring in: the fail chance line and "Not washed: Gold Ring ...". | The dishwasher shows its real odds and what it skips. |
| 03 | `fm-invoices.png` | Tab, Notes page, press R: the Invoices list. Best with at least one unpaid invoice (they come in the post). | Every unpaid invoice in one list. |
| 04 | `fm-scam.png` (optional) | Only if a scam invoice letter has arrived: select it in the inventory, the item panel shows the scam tag. | Scam invoice letters are marked. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
