# Vital Signs art

No art in 0.1.0. Everything the mod draws in game is either a game asset used at runtime, text inside the game's own UI, or plain IMGUI in the house palette.

| Use | Game asset | Treatment |
|---|---|---|
| Vital Signs window, notes (toasts) | none (IMGUI) | Save Guard's skin via Quest Helper (`Core/Skin.cs`): charcoal panel `#1f1f1f` at 96%, rows `#2b2b2b`, selected tab `#835213`, text `#cacaca` / `#bcbab1`, dim `#8a8a88`, one amber accent `#ffab00` (warnings, last death). Notes stack under Save Guard's and Quest Helper's in the same column (width 520). |
| Tooltip lines | the game's stat tooltip (`PlayerStatsToolTipInfo` details text, TMP) | Appended under the vanilla details at `<size=85%>`, ASCII only. The stat title is never changed. |
| Character page hint "X) VITAL SIGNS" / "A) VITAL SIGNS" | the game's own controls hint line (`InventoryNavigationHandler.controlsInfoText`) | Built with the game's `KeyCodeInfoToString`, same font and style as the vanilla hints. |
| Pause menu entry "Vital Signs" (optional, off by default) | clone of the pause menu's Resume button (`PauseMenu.resumeMenuButton`), as Safe Travels and Quest Helper do | Relay removed, child relays not saved, localization removed, TMP autosize 10 to the source size. Placed after Quest Helper's entry (or "Difficulty"), above Safe Travels' entries. |
| Mental health HUD icon (optional, off by default) | the game's own icon (`PlayerStatsUI` index 9) | Kept visible; no change to the sprite. |

## Nexus images

Render with `mods/VitalSigns/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots, `PARTIAL=1` skips missing shots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080, with the tagline under the title) and one `gallery-NN.jpg` per shot, in the order below. Template elements come from the kit: `art/STYLE.md` and `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-17; every caption is at most 56 characters so it renders at full size (checked with a placeholder render).

Screenshots are the user's own captures from a test character at 2560×1440 (system screenshots), UI scale 1.0, kept outside git in `~/Pictures/obenseuer-nexus/VitalSigns/` (copied from `~/Pictures/Screenshots/`). Crop a shot when its UI text comes out small, keep the original as `*-full.png` and record the crop below.

**Status: waiting for screenshots** (shot list 2026-11-17). `nexus/` is empty until `render.sh` runs. A test result that changes the listing can change a caption too.

Tagline: "Find out why you're losing health and mood." Key shot (header and thumbnail): `vs-key.png`, a gameplay screenshot with the HUD hidden (P, no windows): a few OS Energy Drink cans (`add_item 20110 3`, dropped) on a table, in the right third of the frame.

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `vs-character.png` | Tab, Character page: the hint line at the bottom shows the Vital Signs key (X). | Open it from the Character page with X. |
| 02 | `vs-health.png` | `player_stats tiredness set -100`, `add_item 13380 1` (Coffee), drink it, wait a minute, then open Vital Signs: the Health page with the caffeine overdose and its log line. | Every health loss is logged with its cause. |
| 03 | `vs-mood.png` | `add_item 140020 1` (Antidepressants), take one, then the Mental health page. | See what's raising or lowering your mental health. |
| 04 | `vs-addict.png` | `add_item 8 3` (Osmo Olut cans), drink them, then the Addictions page. | Each addiction shows when it will wear off. |
| 05 | `vs-tooltip.png` (optional) | Hover the Mental health meter on the HUD: the tooltip's extra lines. | HUD tooltips get the same numbers. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
