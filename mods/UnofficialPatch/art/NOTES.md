# Unofficial Patch art

No images yet. The Nexus header, thumbnail and gallery still need rendering:

1. Take in-game screenshots (1920×1080, UI scale 1.0) on a test character, never the real Luchik_Abaza slots:
   - the "Account frozen" notification after a scam payout into a frozen Stalburg account;
   - the Rexus tablet shelf in Interior Bazaar Electronics, restocked;
   - a still or mashing tun offering Fill with exactly 0.1 L after the fix, next to a container;
   - a cat chasing a rat in the tenement;
   - an extension bed during a wall renovation showing ", Unsafe" (Bare walls) or no suffix (Shabby walls).
2. Render with the shared template, for example
   `uv run art/render.py "Unofficial Patch" "Small fixes for confirmed bugs." --shot frozen.png "Frozen means held" --shot shelf.png "Tablets restock" --shot cat.png "Cats hunt again"`
   into `art/nexus/` (header 1400×400, thumbnail 1920×1080, gallery frames). Text on the images goes through the humanizer skill.
3. Record here, per file, which game assets and screenshots it derives from (template element sources: `art/SOURCES.md` at the repo root).

| File | What | Derived from |
|---|---|---|
| (none yet) | | |

## In-game UI

- The mod draws no UI of its own. The only in-game text is the "Account frozen" notification, shown through the game's own notification panel.
