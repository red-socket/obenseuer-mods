# Fair Justice art

No images yet. The Nexus header, thumbnail and gallery still need rendering:

1. Take in-game screenshots (1920×1080, UI scale 1.0) on a test character, never the real Luchik_Abaza slots:
   - the Bazaar jail cell with the sentence panel at the top right (a record with two crime types, so the panel shows two charge rows);
   - the same panel while sleeping in the cell (ShowWhileWaiting), with "held until 06:00" if NightRelease is on;
   - the orange bag at the Bazaar overflow point after an arrest with a full inventory, with the "in the orange bag" notification;
   - a worn rucksack window next to the inventory, with a stolen stack moved between them and no crime notification.
2. Render with the shared template, for example
   `uv run art/render.py "Fair Justice" "Jail that plays fair." --shot panel.png "Time left and charges" --shot bag.png "Overflow kept in the bag" --shot backpack.png "Your backpack is not a crime"`
   into `art/nexus/` (header 1400×400, thumbnail 1920×1080, gallery frames).
3. Record here, per file, which game assets and screenshots it derives from (template element sources: `art/SOURCES.md` at the repo root).

| File | What | Derived from |
|---|---|---|
| (none yet) | | |

## In-game UI colours

- Sentence panel (IMGUI): the house palette from `art/STYLE.md`, through `Ui/Skin.cs` (copied from Save Guard and Better Tenement): charcoal `#1f1f1f` at 96 %, title amber `#ffab00`, text `#cacaca`, the place name in small print `#8a8a88`.
- Notifications use the game's own notification panel; Fair Justice draws nothing else in the game's UI.
- The "·" separator is in Unity's built-in IMGUI font.
