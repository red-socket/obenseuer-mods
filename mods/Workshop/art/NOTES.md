# Workshop art

Nothing is shipped as an image file. Everything the mod draws in game is a game asset used at runtime.

| Use | Game asset | Treatment |
|---|---|---|
| Fetch and repeat buttons | clones of the recipe panel's craft button (`RecipeInfoUI.createButtonEnabled`, the `createButtonPrefab` instance in `CraftButtonSlot`), Relay removed, controller glyph hidden | Unchanged look; text from `Core/Strings.cs`, TMP autosize 10-15. Fetch 166×40 (236×40 alone), repeat 64×40, in a row 6 px apart at y 84-124 of the RecipeInfo. Dimmed with a CanvasGroup (alpha 0.5) when there is nothing to fetch. |
| Extra vessel slot backdrops | clones of `SlotNotAvailable` under the vessel panel's `Manufacture inventory slots / Not available slots` | Named `WS_SlotNotAvailable`, 55×55, placed like the authored 9 (49.5 + 69·col, −49.5 − 69·row). |
| Texts with × and · | the game's TMP fonts | Checked: `Erika Ormig SDF` (all variants), `Roboto-Regular SDF` and `LiberationSans SDF` have U+00D7 and U+00B7 in their character tables. |

## Nexus images

Render with `mods/Workshop/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline (the listing's own) went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/Workshop/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. The in-game tests in `.internal/test-plan.md` have not run yet; a test result that changes the listing can change a caption too.

Tagline: "Less walking between the crates and the workbench." Key shot (header and thumbnail): `ws-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): the table saw with crates beside it, in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `ws-fetch.png` | `add_furniture Table Saw 1`, `add_furniture Metal Crate 1`, `add_item 13450 10` (Wood Log). Place both in your flat a few steps apart, put the logs in the crate, open the saw and pick Planks: the Fetch ingredients button above Craft. | Fetch ingredients brings in what's missing from crates. |
| 02 | `ws-repeat.png` | After Fetch: the small repeat button next to it, pick 5, then Craft. The button reads "Stop (4)". | Pick how many to make, and the machine keeps going. |
| 03 | `ws-hint.png` | Menu closed, looking at the saw: "U: repeat Planks". | Press U at a machine to make its last recipe again. |
| 04 | `ws-cancel.png` | `add_furniture Dishwasher 1`, `add_item 15350 1` (Dishwasher Powder), `add_item 18 1` (Bag of Green Mushrooms). Start a wash, then click the running job once: the confirm note. | Click a running wash twice to get the item back. |
| 05 | `ws-vessel.png` (optional) | `add_furniture Mashing Barrel 1`, `add_item 20190 20` (Bunch of Red Grapes); open the barrel and pick Red Grape Mash: the "Fetch for ×k" button. | On a brewing vessel it fetches for the biggest batch. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
