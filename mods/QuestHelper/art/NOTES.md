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

Render with `mods/QuestHelper/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline (the listing's own) went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/QuestHelper/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. The in-game tests passed on 2026-11-24 (`.internal/test-results-pass3.md`) and changed no caption. Still missing: all five screenshots and the key shot (`qh-key.png`); `~/Pictures/obenseuer-nexus/QuestHelper/` does not exist yet.

Tagline: "For when a quest stops making sense." Key shot (header and thumbnail): `qh-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): someone who gives quests (Toivo in the tenement, for example) in the right third of the frame. Keep hints at their default (off) in every shot, so no image shows a spoiler.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `qh-pause.png` | Esc: the pause menu with Quest Helper below Difficulty. | Open Quest Helper from the pause menu. |
| 02 | `qh-doctor.png` | Pick Quest Helper in the pause menu: the Stuck quests page. With a stuck save the problem's page is better; a clean save still shows the checks. | It backs up your save before it fixes a stuck quest. |
| 03 | `qh-journal.png` | Take on any new objective (talk to a quest giver), then Tab, Tasks, select it: the "Received from ..." line under it. | A line under each objective says where you got it. |
| 04 | `qh-pin.png` | Right after 03, the Map tab: the amber pin where you got the objective. | The task you last opened gets an amber pin on the map. |
| 05 | `qh-tbc.png` (optional) | Only in a save where a story has reached "cannot be completed": the Tasks page with the To be continued section. | Stories that stop for now go under To be continued. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
