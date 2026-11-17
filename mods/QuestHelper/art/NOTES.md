# Quest Helper art

No art ships. Everything the mod draws in game is either a game asset used at runtime or plain IMGUI in the house palette.

| Use | Game asset | Treatment |
|---|---|---|
| Pause menu entry "Quest Helper" | clone of the pause menu's Resume button (`PauseMenu.resumeMenuButton`), the same way Safe Travels makes its entries | Relay removed, child relays not saved, localization removed, label from `Core/Strings.cs`, TMP autosize 10 to the source size. Placed after "Difficulty". |
| Quest Helper window, toasts | none (IMGUI) | Save Guard's skin: charcoal panel `#1f1f1f` at 96%, rows `#2b2b2b`, hover `#3a3a3a`, selected/focused `#835213`, text `#cacaca` / `#bcbab1`, dim `#8a8a88`, one amber accent `#ffab00`. Controller and keyboard focus uses the selected colour. |
| Map pin | the journal's own active-task icon (`TaskUIPanel.statusActiveUnselected` on `TaskPanel.taskPrefab`) in a clone of the map's landmark template (`Map.LandmarkPrefab`, `GameObject/LandMark.prefab`) | Tinted amber `#ffab00`, `PinSize` px square (default 26). Button removed; hover shows "Quest: <objective>". |
| "To be continued" header | clone of the task list's "Finished tasks" title (`TaskPanel.finishedTaskTitlePrefab`) | Text "+ / - To be continued (n)" (ASCII, the font has no triangles); a transparent Image and a Button for the click; the game's own ToolTip on hover. |
| Journal lines under objectives | the objective row's own TMP text (`ObjectiveUI.objectiveTitle`) | Appended at 80% size in `#BFBFBF`. |

## Nexus images

**Status: not started.** Header, thumbnail and gallery follow the house kit once the in-game tests have passed. Suggested shots, test IDs from `.internal/test-plan.md`:

| # | Screenshot | Set up |
|---|---|---|
| 01 | `qh-pause.png` | Pause menu with "Quest Helper (1)" after Difficulty (F1) |
| 02 | `qh-doctor.png` | Rat problem detail page in the canteen, "Stuck" (B1) |
| 03 | `qh-journal.png` | Tasks page with "Received from" and a tier 2 hint under Permission to dig (D1, D3) |
| 04 | `qh-tbc.png` | Task list with "- To be continued (1)" (E1) |
| 05 | `qh-pin.png` | Mines map with the amber pin on Service Area (D5) |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
