# Display Options art

No art in 0.1.0. Everything the mod shows in game is a game asset used at runtime or text inside the game's own UI.

| Use | Game asset | Treatment |
|---|---|---|
| "UI Scale" row in Pause > Options > Quality | runtime copy of the game's FOV row (`OptionsMenu.fovSlider`'s row, `GameObject/Pause Menu.prefab`: `Menu/Display/Fov Option`) | Same frame, slider, handle, font and sounds. Label text replaced, number text shows 75-150. The copy's Relays don't save, its localisation is removed and its slider event is new (it never calls `setFOV`). |

## Nexus images

**Status: not started.** Header, thumbnail and gallery follow the house kit once the in-game tests have passed. Suggested shots, test IDs from `.internal/test-plan.md`:

| # | Screenshot | Set up |
|---|---|---|
| 01 | `do-row.png` | Pause > Options > Quality with the UI scale row selected (M1) |
| 02 | `do-ui-110.png` / `do-ui-100.png` | The inventory at 100% and 110% on 16:9 (U2) |
| 03 | `do-dialogue-uw.png` | A conversation on 21:9 with UltrawideDialogue off and on (U3) |
| 04 | `do-shadows.png` | Mallment, shadow preset Off and Balanced side by side, with the FPS from the S table (S1) |
| 05 | `do-effects.png` | Drunk effect with LensDistortion and DepthOfField on GameDefault and Off (E2) |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
