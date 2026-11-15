# Better Tenement art

No images yet. The Nexus header, thumbnail and gallery still need rendering:

1. Take in-game screenshots (1920×1080, UI scale 1.0) on a test character:
   - the Tenement resources chest at 176 slots, scrolled part way, with the Stash button and the "97/176" counter in the header;
   - an upgrade card with the Ledger line ("+4000 value · +50 OC +5 RM/W · By Rob") and a "~" amber count;
   - the Ledger panel beside the Tenement tab, with a shopping list;
   - the Tenement Doctor window with outlines visible through a wall.
2. Render with the shared template, for example
   `uv run art/render.py "Better Tenement" "Landlording without the bookkeeping." --shot chest.png "Bigger chests, Stash button" --shot ledger.png "Rent change per upgrade" --shot doctor.png "What keeps a flat uncleared"`
   into `art/nexus/` (header 1400×400, thumbnail 1920×1080, gallery frames).
3. Record here, per file, which game assets and screenshots it derives from (template element sources: `art/SOURCES.md` at the repo root).

| File | What | Derived from |
|---|---|---|
| (none yet) | | |

## In-game UI colours

- IMGUI windows (Ledger panel, Doctor window): the house palette from `art/STYLE.md`, through `Ui/Skin.cs` (copied from Save Guard): charcoal `#1f1f1f` at 96 %, rows `#2b2b2b`, selected row `#835213`, text `#cacaca`, small print `#8a8a88`, accent and warnings amber `#ffab00`.
- In the game's own TMP UI:
  - chest counter `#B8A27A` (a muted tan close to the panel's header text), amber `#FFAB00` above 88 stacks;
  - card count amber `#E0B050` (carried items will be used); red shortage stays the game's own;
  - Ledger deltas green `#9FD18B` / red `#E06060` (the card background is light, so red is readable there, unlike on charcoal).
- Separators "·" and "–" are in the game's Erika Ormig, Roboto and LiberationSans TMP fonts; "✓" is not, so the panels write "met".
- Doctor outlines default to `#FFAB00` (`Doctor.OutlineColor`).
