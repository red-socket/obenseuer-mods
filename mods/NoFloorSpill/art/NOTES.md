# No Floor Spill art

No images yet. The Nexus header, thumbnail and gallery still need rendering:

1. Planned icon: the Ash item sprite (AssetRipper `Sprites/Items/`, item 10010's `Appearance.SpritePath` in `StreamingAssets/Items.json`) over a stove, for example the Metal Stove or the greenhouse barrel stove.
2. Take in-game screenshots (1920×1080, UI scale 1.0) on a test character, never the real save slots:
   - a stove with a full ash slot and a clean floor beside it;
   - the same stove in Vanilla mode with an ash pile, for a before/after pair;
   - a chest next to a stove holding the moved ash (NearbyStorage);
   - a full machine with the Plastic Trash in the freed slot.
3. Render with the shared template (`art/render.py` at the repo root) into `art/nexus/` (header 1400×400, thumbnail 1920×1080, gallery frames). Text on the images goes through the humanizer skill.
4. Record here, per file, which game assets and screenshots it derives from (template element sources: `art/SOURCES.md` at the repo root).

| File | What | Derived from |
|---|---|---|
| (none yet) | | |

## In-game UI

- The mod draws no UI and shows no notifications. Its only feedback is the BepInEx log.
