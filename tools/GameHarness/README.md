# Game Harness

A developer tool for testing our mods. It lets a script (or an AI coding session) play Obenseuer on a Linux desktop where the game runs under Proton. It can start the game, load a test save, move, look, press keys, run F1 console commands, click UI, read game state, take screenshots and quit.

It has two parts:

- `GameHarness.dll`, a BepInEx plugin that listens on `127.0.0.1:47913` (localhost only) and runs commands on the game's main thread.
- `harness.py`, a stdlib-only Python CLI that talks to the plugin and to Steam.

Paths below use `GAME` for the Steam install folder and `USERDATA` for the game's `LocalLow/Loiste Interactive/Obenseuer` folder inside the Proton prefix.

It is internal. It never goes on Nexus and never goes into a mod archive. It lives in `tools/`, not `mods/`, so the per-mod packaging never picks it up.

## Why in-game control

It was built on GNOME under Wayland, where xdotool and ydotool were not installed. Rather than depend on OS-level input tools, the plugin simulates all input inside the game, which works the same on any desktop:

- Key presses are OR-ed into Unity's legacy `Input.GetKey*`, `GetMouseButton*`, `anyKey` and `anyKeyDown`. The game's `InputManager`, the F1 console check and BepInEx `KeyboardShortcut` hotkeys all read through those, so game actions and mod hotkeys work.
- Looking around sets the camera controller's yaw and pitch directly. Teleporting uses the game's own `PlayerLocator.Teleport`.
- UI clicks send uGUI pointer events to the matching element.
- Screenshots use `ScreenCapture` at the end of a frame and write a PNG through Wine's `Z:` drive.

The harness never sends real mouse or keyboard events.

## Setup (once)

```bash
python3 -I tools/GameHarness/harness.py install
python3 -I tools/GameHarness/harness.py test-save --from Helmi_Betoni/Slot1
```

`install` builds the plugin and copies it to `GAME/BepInEx/plugins/GameHarness/`. Run it only while the game is closed. `uninstall` removes that folder again.

`test-save` takes a read-only snapshot of a slot from one of the user's characters and installs it as `Saves/Harness_Test/Slot1`. Run it again without `--from` to reset the test character to that snapshot. The snapshot lives in `~/.cache/obenseuer-harness/base/`.

## How the user's saves stay safe

- The snapshot has its player identity renamed to "Harness Test" (in `Globals.tnmt`, every `.tnmt` header and the dialogue Lua). The game picks the save folder from the player's name, so every save, autosave and door transition of that copy goes to `Saves/Harness_Test/`.
- `load` only accepts the test character, and once any command has arrived, the plugin blocks every load of another character, wherever it comes from (the load menu, a door, `change_scene`). All of them go through `SaveController.LoadGameWithMigration`. A blocked load is logged as an error, counted in `state` as `blockedLoads`, and the screen fades back in. A menu bug once switched the load panel to the player's own character, and a click loaded it.
- Once any command arrives, the plugin arms a guard: a Harmony prefix on `SaveController.SaveGame` blocks any save whose character is not `Harness_Test` and logs an error. `state` reports the count as `blockedSaves`.
- `checklist` lists the size and modification time of every file under `Saves/` outside `Harness_Test` before and after the run, and fails if anything changed.
- `launch` refuses to start while any `Obenseuer.exe` is running. Commands that act on the game refuse to run if the running game is not the one `harness.py` launched (the PID is checked against `~/.cache/obenseuer-harness/session.json`).

The harness also keeps tests off the player's Steam account. The game only stops achievements while the real F1 console is open, and `console` runs commands without opening it, so `console add_crime 1500` once unlocked a real achievement. Once a session is armed, the plugin blocks every achievement unlock and progress update (the game's `Achievements.TriggerAchievement` overloads) and, as a backstop, every write on Steamworks' `SteamUserStats` (`SetAchievement`, `SetStat`, `StoreStats` and the rest). Each blocked unlock is logged as a warning, and `state` reports the total as `blockedAchievements`, with counts per name in `blockedAchievementNames`. The test save does not record the unlocks either.

The game's global settings files in `USERDATA` (`Display.json`, `Quality.json`, `Controls.json`, `Volume.json`, and `KeyBindings.json` if present) are not per save, so a harness game could change the user's settings. One relaunch after a kill came up at 320x200 and wrote that into `Display.json`. `harness.py` guards them:

- Before every launch attempt it copies them to `~/.cache/obenseuer-harness/settings/`.
- After every `quit`, `kill` and watchdog kill it compares them with that copy, puts back any that changed (or removes one the game created), and prints one `warning: restored USERDATA/<file>` line per file.
- If a harness game died without that check, the next `launch` restores from the copy first. Otherwise `launch` takes a fresh copy, so settings the user changed in their own game are kept.
- `settings check` compares the files with the copy, `settings restore` puts them back (game closed), and `settings seed` takes a new copy of the current files.
- `checklist` adds a "User settings unchanged" step that fails if any file had to be restored during the run.

Two side effects to know about. `Saves/Harness_Test` shows up in the in-game load menu as "Harness Test". Steam Cloud also syncs it, like every folder under `Saves/`.

## Commands

Every command below is `python3 -I tools/GameHarness/harness.py <command>`. Errors exit with status 2 and print `error: ...`.

### Game lifecycle

| Command | What it does |
|---|---|
| `launch [--cooldown S] [--retries N] [--timeout S]` | Starts the game through `steam -applaunch 951240` (so the Proton launch options apply) and returns once the main menu buttons are up, usually after 15 to 25 s. See "Hangs and recovery" below for the cooldown, the title screen and the watchdog. The game uses the user's own display settings (fullscreen 2560x1440 here). Unity's `-screen-*` arguments don't help, because the game applies `Display.json` over them. |
| `wait [--scene NAME] [--menu] [--timeout S]` | Waits until a level is loaded and playable (optionally a given scene), or until the main menu is ready. Prints the state. |
| `quit [--force]` | Waits for any pending map write, then ends the process the same way the game's own Quit button does (`Process.Kill`). Returns once the process is gone. If the plugin does not answer, or the game is still running 60 s later (15 s with `--force`), it kills the game instead. `--force` also quits during a save or load. |
| `kill` | Kills the game `harness.py` launched, right away. It never touches a game it did not launch. |

### Saves

| Command | What it does |
|---|---|
| `load Slot1` | Loads `Saves/Harness_Test/<slot>` from the menu or in game (fade, then `SaveController.LoadGameWithMigration`, as the load menu does). It returns at once, so follow it with `wait`. |
| `save Slot2` | `SaveController.SaveGame("Slot2")`, the same call the save menu makes. Only works for the test character. |

### Reading state

| Command | What it does |
|---|---|
| `state` | Prints JSON with the fields listed below. |
| `mods` | Lists the loaded BepInEx plugins (GUID, name, version). |
| `log [--source bepinex\|player\|both] [--errors] [--warnings] [--grep RE] [--lines N]` | Tails `GAME/BepInEx/LogOutput.log` and/or `USERDATA/Player.log`. Both files are rewritten at each launch. `--errors` keeps `[Error]` and `[Fatal]` lines, `--warnings` keeps `[Warning]` lines; give both for both. |
| `find NAME [limit]` | Finds active scene objects whose name contains NAME, nearest first, with positions. `find type:InteractableFurnitureDisplay` matches by component class instead. |
| `doors` | Lists level-change doors, nearest first: target scene, entrypoint, position, locked. |
| `buttons [filter]` | Lists the clickable UI elements on screen, with their labels. |

`state` returns:

- `scene`, `loading`, `fading`, `waitingForKey` (the title screen's "press any key"), `mainMenuReady` and `inGame`;
- `save`: the slot folder, character and slot;
- `position`, plus `look` (yaw and pitch);
- `aim`: what the centre of the screen hits, and how far away it is;
- `target`: the interactable under the crosshair, with its title and use message;
- `useText`: the look-at text the HUD shows right now (name line and use line). It is read before the PT-BR mod translates it on screen;
- `time`: clock, day, month and day counter;
- `ui`: cursor, pause, console, menu, inventory, storage, HUD visibility and control locks;
- `fps`, `focused`, `heldKeys` and `blockedSaves`.

### Acting

| Command | What it does |
|---|---|
| `console add_item 12 5` | Runs an F1 console command without opening the console (so achievements are not suspended). Returns the lines the console printed and any Unity log lines from the call. Commands added by our mods' test builds work the same way. |
| `key E [seconds]` | Presses a Unity `KeyCode` (`E`, `Tab`, `Escape`, `F4`, `Alpha5`, `Mouse0`, ...). Without seconds it is a single tap. |
| `action Interact [seconds]` | Presses whatever key a game action is bound to: `Interact`, `Inventory`, `Menu`, `Map`, `Tasks`, `Pause Menu`, `Hide Hud`, `Primary Use`, ... |
| `move forward 1.5` | Holds a movement action: `forward`, `backward`, `left`, `right`, `jump`, `crouch` or `walk`. |
| `release` | Releases every held virtual key. |
| `look YAW [PITCH]` | Sets the absolute view in degrees: yaw 0 faces +Z and 90 faces +X; positive pitch looks up. The game clamps pitch to about ±60. |
| `turn DYAW [DPITCH]` | Turns relative to the current view. |
| `lookat X Y Z` | Faces a world point. |
| `tp X Y Z [YAW] [PITCH]` | Teleports the player. |
| `goto-door N` | Stands on door N's own arrival point and faces it. |
| `door N` | Uses door N: the same `Interact()` the E key triggers, including the lock check, the save and the level change. |
| `click LABEL` | Clicks a UI element, matching first by exact label, then by part of the label, then by object path. |
| `cmd NAME ARGS...` | Sends any plugin command as is (the same set as above, by plugin name, e.g. `cmd goto_door 0`). |

### Screenshots

```bash
python3 -I tools/GameHarness/harness.py shot NAME [--mode full|nohud|clean] [--out DIR]
```

This prints the PNG path, which you can open with the Read tool to look at it. The default folder is `~/.cache/obenseuer-harness/shots/`.

- `full`: what the player sees.
- `nohud`: the game's own HUD hidden (the same as pressing P), while mod windows and menus stay visible.
- `clean`: every screen-space canvas hidden, so no HUD, crosshair, menus or mod windows. Use it for header art. IMGUI windows are not hidden.

### Mods and the release checklist

| Command | What it does |
|---|---|
| `mod-off MOD` / `mod-on MOD` | Moves `GAME/BepInEx/plugins/MOD` to `GAME/BepInEx/plugins-harness-off/` and back. The game must be closed. Refuses `ObenseuerPTBR` and `GameHarness`. |
| `checklist MOD [--out DIR] [--cooldown S]` | Runs the CLAUDE.md release checklist for one installed mod on the test save. See below. |

The checklist steps are:

1. Fresh launch: the mod is loaded, and the BepInEx log has no error lines from it.
2. Load `Slot1`.
3. Go to the nearest unlocked door and go through it.
4. Save to `Slot2`, then load `Slot2`.
5. Quit, park the mod, launch, load `Slot2` and check that the same scene comes up. The mod is always put back, even when a step fails.

Only `[Error]` and `[Fatal]` lines count as errors. Warnings, such as HarmonyX's `AccessTools.TypeByName: Could not find type named X`, are counted and listed but never fail a step. If the game freezes while launching or loading (steps 1, 2 and 5), the checklist kills it, cools down, relaunches and loads again once, and notes that in the report. A freeze in steps 3 or 4 fails the run.

After that it checks that no other character's save files changed. It takes a screenshot at each step and writes `report.md` (PASS/FAIL per step, with log excerpts) next to the screenshots. It exits with status 1 on failure.

## Typical session

```bash
H="python3 -I tools/GameHarness/harness.py"
$H launch
$H load Slot1 && $H wait
$H console add_money OC 500
$H doors
$H goto-door 0 && $H shot at-door
$H action Inventory && sleep 1 && $H shot inventory && $H key Escape
$H shot header --mode clean
$H log --errors
$H quit
```

## Hangs and recovery

Under Proton the game sometimes deadlocks right after the main menu loads, especially when it starts within about a minute of the previous game closing: 0% CPU, both logs stop, and the plugin never answers. `harness.py` handles this as follows:

- `launch` first waits until 60 s (`--cooldown`) have passed since the last game exit. That is the later of the harness's own recorded quit or kill and the last write to `Player.log` or `LogOutput.log`, so a game the user just closed counts too.
- If the plugin does not answer within 120 s of the process starting, the game exits, or the main menu does not come up in time, `launch` kills the game it started, cools down and tries again (`--retries`, default 1).
- Every wait (`wait`, the loads in `checklist`) fails with "stopped answering ... (frozen)" once a game that answered before has been silent for 45 s, instead of waiting out its whole timeout.
- On the title screen, `launch` presses Return, then Space, then the left mouse button, 4 s apart, and then calls the title screen's own keyboard handler (`cmd dismiss_title`).
- The game's processes are the ones with an argument that is a path ending in `\Obenseuer.exe` or `/Obenseuer.exe`: the Windows process and its Steam and Proton wrappers. A shell or `grep` whose command line only mentions the name does not count. The harness records the PIDs it launched in `~/.cache/obenseuer-harness/session.json`, and kills only those.

## Display

The game needs a screen to render to. With the monitor off, asleep or unplugged, a normal Steam launch starts at 1024x768 and every screenshot is 1x1.

`launch --headless [--size 2560x1440]` (and `checklist --headless`) avoids that. The harness starts the game itself inside [gamescope](https://github.com/ValveSoftware/gamescope)'s headless backend, which gives it an off-screen display of that size. The game never shows on the user's screen, and it keeps working while the screen is locked or asleep. Install gamescope with `sudo pacman -S gamescope`.

- Headless mode runs the same command Steam would: the Steam Linux Runtime that Proton's `toolmanifest.vdf` asks for, then `Proton - Experimental` (override with `OBENSEUER_PROTON`), then `Obenseuer.exe`, with Steam's app ID variables set. Steam must be running (the game's Steamworks init needs it).
- It never reads or changes Steam's launch options or settings. The one launch option the game needs, `WINEDLLOVERRIDES="winhttp=n,b"` for BepInEx, is passed directly. Normal Steam launches are unaffected. To go back, leave out `--headless`, or uninstall gamescope.
- `quit`, `kill` and the watchdog also stop the gamescope process. Its output goes to `~/.cache/obenseuer-harness/gamescope.log`.
- The game's sound still plays through the user's speakers.

Without `--headless`, `launch` checks the screen and either switches or fails fast:

- When GNOME's `org.gnome.Mutter.DisplayConfig.GetCurrentState` lists no monitor, `launch` switches to headless by itself if gamescope is installed, and otherwise stops with "no usable display".
- Once the menu is up, a probe screenshot smaller than 640x360 stops the launch and kills the game.
- `shot` refuses captures under 640x360 the same way.

## Notes and limits

- The plugin keeps `Application.runInBackground` on (config `[Harness] RunInBackground`), so it keeps answering while another window has focus. Turn that off if a test needs the game's unfocused behaviour.
- Only one request runs at a time per connection. Long actions (`load`, `door`) return at once: poll with `wait` or `state`.
- Plugin DLLs cannot be swapped while the game runs. Adding a brand-new plugin folder mid-run is safe; replacing a loaded one is not. Quit, then `install` or redeploy.
- Mouse look through virtual mouse deltas is not simulated. Use `look`, `turn` and `lookat`.
- The plugin does nothing until the first command arrives. It only keeps a listening socket, applies the background setting, and adds postfixes that return straight away while no virtual key is pressed. Even so, remove it (`uninstall`) before playing normally.
