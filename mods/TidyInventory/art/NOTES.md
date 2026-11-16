# Tidy Inventory art

Nothing is shipped as an image file. Everything the mod draws in game is a game asset looked up at runtime.

| Use | Game asset | Treatment |
|---|---|---|
| Pin badge | `ASSETS/Sprite/locked.asset` (texture `ASSETS/Texture2D/locked.png`), found by sprite name and texture name with `Resources.FindObjectsOfTypeAll<Sprite>()` | Tinted #D9A441 for a filled or reserved pin, #8C8C8C for foreign content; drawn at 14×16, top centre of the slot, `preserveAspect`. |
| Badge fallback | `ASSETS/Sprite/icon_owned.asset` | Same tint. If neither sprite is found, a plain tinted square (one warning in the log). |
| Ghost icon | each pinned item's own `Appearance.GetSprite(null)` | White at `GhostOpacity` (0.35), stretched over the slot's Padding, under the item. |
| Empty / storage Combine buttons | clones of the storage panel's Take all button | Unchanged look; label text from the config. |
| Inventory Combine button | clone of the inventory `Sort button` (40×40, itself a text button labelled "Sort") | Text label "Combine" from `Combine.InventoryLabel` (autosize 7-12), no icon; hover tooltip through the game's own `ToolTip`. If a game sprite replaces the text later, record it here. |

Rejected: `marked_item_001` (already means "marked for upgrade") and the star sprites (they mean quality).

## Nexus images

Render with `mods/TidyInventory/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline (the listing's own) went through the humanizer skill on 2026-11-16; all of them fit at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/TidyInventory/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below. Test IDs refer to `.internal/test-plan.md`.

**Status: waiting for screenshots.** `nexus/` is empty until `render.sh` runs.

Tagline: "Keep your inventory where you put it." Key shot (header and thumbnail): `ti-key.png`, a gameplay screenshot with the HUD hidden (no UI windows): the blue rubber duck (11) dropped on a table or shelf, in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `ti-pin.png` | Inventory open, a stack pinned with the middle mouse button: the padlock badge on its slot | Middle-click a slot to pin it, and Sort leaves it alone. |
| 02 | `ti-ghost.png` | A pinned single item used up (e.g. White Bread 10650 eaten): the empty slot with its faded picture and the badge | An empty pinned slot keeps a faded picture of its item. |
| 03 | `ti-empty.png` | A street dumpster in Open Sewer Bazaar opened, Empty clicked once so it reads "Sure?" | Click Empty twice to clear a trash bin. |
| 04 | `ti-combine-before.png` | `add_item 140010 3`, one pill taken from each pack (select the pack, use it): three part-used packs, the Combine button beside Sort | Before: three part-used packs of painkillers. |
| 05 | `ti-combine.png` | Same inventory right after Combine | After Combine: full packs and one part-used pack. |
| 06 | `ti-duck.png` (optional) | Only after Mr. Duck's quest (T-D2): the blue duck hovered, Delete pressed once | Once Mr. Duck's quest is done, the blue duck can go. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
