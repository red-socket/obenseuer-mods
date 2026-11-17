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

**Status: not started.** Header, thumbnail and gallery follow the house kit once the in-game tests have passed. Suggested shots, test IDs from `.internal/test-plan.md`:

| # | Screenshot | Set up |
|---|---|---|
| 01 | `vs-health.png` | Health page during a caffeine overdose, with the hidden-damage forecast and the log (H1) |
| 02 | `vs-death.png` | The death note after a hidden-damage death (H14) |
| 03 | `vs-mood.png` | Mental health page with the HUD breakdown and an antidepressant wearing off (M1, M5) |
| 04 | `vs-tooltip.png` | Mental health tooltip with the extra lines (M7) |
| 05 | `vs-character.png` | Character page with "X) VITAL SIGNS" in the hint line (U1) |

| File | Derived from |
|---|---|
| (filled in after the final render) | |
