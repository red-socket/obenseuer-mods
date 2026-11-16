# Time Keeper art

Nothing in this folder ships: the mod draws no art of its own. The waiting clock is the game's own sleep clock (`Game Logic/Game UI/Game UI/Waiting/WaitingUI` in `AssetRipper/ExportedProject/Assets/Resources/prefabs/main/Game Logic.prefab`) with two runtime text changes: the label reads "Waiting" instead of "Sleeping", and the start button (`Controls/Sleep Buttons/Sleep button/Button Text`) reads "Wait" instead of "Sleep" while Time Keeper's clock is open. The weekday is the game's own HUD clock text (`ShowTime`).

**Status: waiting for screenshots and the logo.** Render with the house kit (`art/SOURCES.md` at the repo root) once the shots exist; captions go through the humanizer skill first.

## Logo

Planned derivation (record the exact exported files here once made):

| Element | Derived from |
|---|---|
| Watch faces | the watch item icons: a mechanical watch (e.g. Gold Mechanical Watch, 12450) and a digital watch (e.g. Gold Digital Watch, 12460), from `AssetRipper/ExportedProject/Assets` (item icon sprites) |
| Clock hands | the sleep clock's hand sprites under `Game UI/Waiting/WaitingUI/SleepingClock` (the `Rot` children) in `Game Logic.prefab` |

## Nexus images

Screenshots are the user's own captures from a test character at 1920×1080, UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/TimeKeeper/`. Test IDs refer to `.internal/test-plan.md`.

| # | Screenshot | Set up | Caption (draft) |
|---|---|---|---|
| 01 | `tk-weekday.png` | W1: a mechanical watch worn, inventory closed, the HUD clock reading "Wednesday 18:20" (any weekday) | Every watch you wear shows the day. |
| 02 | `tk-wait.png` | T1: the waiting clock open, label "Waiting: 01:30", the Wait button visible | Pass time awake on the game's own clock. |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
