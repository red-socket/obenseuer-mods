# Green Thumb art

Nothing in this folder ships: the mod draws no art of its own. Its in-game lines are plain text inside the game's own prompt (`UseText`, TMP), and the quick-plant bar and preview it hides are the game's own.

| Use | Game asset | Treatment |
|---|---|---|
| Hold hint "Hold - Keep harvesting, hold longer for everything in reach" | the game's use prompt (`UseText.UseAndHoldText`, the same colour tags) | Replaces vanilla's hold text only; ASCII. |
| Paused line "No power: growth paused", or "Switched off: growth paused" when the grower is switched Off | the plant's world tooltip title (`grow.GetTitle`) | One extra line under vanilla's "Growing …%" line; ASCII. |

## Nexus images

**Status: not started.** Header, thumbnail and gallery follow the house kit (`art/STYLE.md`, `art/render.py` at the repo root) once the in-game tests have passed. Per the repo convention (commit 6f24696) the thumbnail shows the mod's tagline instead of MOD FOR OBENSEUER. Captions and the tagline go through the humanizer before rendering.

Tagline (draft, from the listing): "Less clicking in the garden, and the growers do what they say." Shorten it if it doesn't fit the header.

Key shot and thumbnail are planned from game assets: a render of the Tomato plant prefab growing in a Flowerbox, or a gameplay screenshot of the same with the HUD hidden. Record the exact files once made.

| Element | Derived from |
|---|---|
| Plant | `ASSETS/Resources/prefabs/items/Tomato plant.prefab` |
| Planter | `ASSETS/GameObject/Flowerbox.prefab` (alternatives: `Flowerbox With Lamp.prefab`, `Terrarium.prefab`) |

Suggested gallery shots (test IDs from `.internal/test-plan.md`):

| # | Screenshot | Set up | Caption (draft) |
|---|---|---|---|
| 01 | `gt-harvest.png` | T-H1: a bed of ripe potatoes mid-sweep, the new hold hint visible | Keep holding Use to harvest everything in reach. |
| 02 | `gt-stacks.png` | T-B1: one tomato stack where vanilla would show several | Produce of similar freshness stacks. |
| 03 | `gt-percent.png` | T-G1: a tomato reading "Growing fruits 100%" with the panel badge | The fruit percentage counts up to the harvest. |
| 04 | `gt-power.png` | T-W2: an unpowered terrarium with "No power: growth paused" | Terrariums stop growing without power. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
