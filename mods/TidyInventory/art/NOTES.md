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

Not made yet. Follow the house style kit (`art/SOURCES.md` at the repo root) when they are.
