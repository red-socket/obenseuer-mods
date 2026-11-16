# Time Keeper art

Nothing in this folder ships: the mod draws no art of its own. The waiting clock is the game's own sleep clock (`Game Logic/Game UI/Game UI/Waiting/WaitingUI` in `AssetRipper/ExportedProject/Assets/Resources/prefabs/main/Game Logic.prefab`) with two runtime text changes: the label reads "Waiting" instead of "Sleeping", and the start button (`Controls/Sleep Buttons/Sleep button/Button Text`) reads "Wait" instead of "Sleep" while Time Keeper's clock is open. The weekday is the game's own HUD clock text (`ShowTime`).

Nexus images: see below. The logo is separate work and not started.

## Logo

Planned derivation (record the exact exported files here once made):

| Element | Derived from |
|---|---|
| Watch faces | the watch item icons: a mechanical watch (e.g. Gold Mechanical Watch, 12450) and a digital watch (e.g. Gold Digital Watch, 12460), from `AssetRipper/ExportedProject/Assets` (item icon sprites) |
| Clock hands | the sleep clock's hand sprites under `Game UI/Waiting/WaitingUI/SleepingClock` (the `Rot` children) in `Game Logic.prefab` |

## Nexus images

Render with `mods/TimeKeeper/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-16; all of them fit at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/TimeKeeper/` (copied from `~/Pictures/Screenshots/`). Test IDs refer to `.internal/test-plan.md`.

**Status: waiting for screenshots.** `nexus/` is empty until `render.sh` runs.

Tagline: "See the day on any watch, and wait without a bed." The tagline shortens the listing's "See the day on any watch, and pass time without a bed." so it fits the header. Key shot (header and thumbnail): `tk-key.png`, a gameplay screenshot with the HUD hidden (no UI windows).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `tk-weekday.png` | W1: Gold Mechanical Watch (12450) worn, inventory closed, the HUD clock reading "<Weekday> HH:MM" | Every watch you wear shows the day of the week. |
| 02 | `tk-wait.png` | T1: the waiting clock open after Home, label "Waiting: 01:00", the Wait button visible | Press Home to pass time awake on the game's sleep clock. |
| 03 | `tk-blocked.png` (optional) | Home pressed while too hungry or with the police after you: the note and the red flash | If you can't wait right now, a short note says why. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
