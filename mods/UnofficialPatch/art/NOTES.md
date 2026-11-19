# Unofficial Patch art

## Nexus images

Render with `mods/UnofficialPatch/art/render.sh` (wraps `art/render.py`; `SHOTS=dir` points it at another folder, `PLACEHOLDER=1 OUT=dir` checks text fit without screenshots). Outputs go to `nexus/`: `header.jpg` (1400×400), `thumbnail.jpg` (1920×1080) and one `gallery-NN.jpg` per shot, in the order below. Template elements (fonts, grain, elevation line art, logo) come from the kit: `art/SOURCES.md` at the repo root. Captions and the tagline went through the humanizer skill on 2026-11-15.

Screenshots are captures from a test character at 2560×1440, UI scale 1.0 (the user's system screenshots, except `up-coffee.png`, a harness capture), kept outside git in `~/Pictures/obenseuer-nexus/UnofficialPatch/` (copied from `~/Pictures/Screenshots/`). The test IDs refer to `.internal/test-plan.md`.

**Status: render 2026-11-19.** Header, thumbnail, the `up-shelf` and `up-coffee` frames and the fixes card are final. `up-frozen` and `up-still` need a staged save state that the console can't set up, so they are optional now and probably won't be taken; `up-cat` is optional too.

Tagline: "Small fixes for confirmed bugs." Key shot (header and thumbnail): `up-key.png`, a gameplay screenshot with the HUD hidden (no UI windows).

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `up-frozen.png` (optional) | Test S-033 hold, the "Account frozen" notification | A scam payout into a frozen account stays in that account. |
| 02 | `up-shelf.png` | Test S-101, the Rexus tablet shelf filled, HUD hidden, wide shot of the shop | The Rexus tablet shelf restocks again. |
| 03 | `up-still.png` | Test S-051 machine, the still offering Fill with 0.1 L | A still stuck with 0.1 L of liquid can be drained again. |
| 04 | `up-coffee.png` | Test S-093, coffee in a fridge with the "refrigerated or frozen" tooltip | Coffee keeps in the fridge. |
| 05 | `up-cat.png` (optional) | A cat chasing a rat in the tenement | Cats are no longer stopped from hunting rats. |
| 06 | `fixes-card.svg` | No screenshot: `render.sh` renders the card, then frames it | Every fix has its own switch. |

`fixes-card.svg` is our own layout in the house style (game fonts Erika Ormig and Perfect DOS VGA 437, `art/textures/concrete.png` grain). Its items follow the defaults in `Cfg.cs` and the issue numbers in `NEXUS.md`; update it when a fix is added or a default changes. Updated 2026-11-15 for the NPC door and cabin door fixes ("Police open doors as they pass", "Porta-potty doors stay open" on; "Doors open for every NPC" off). Reworded 2026-11-19 to match what was tested: "Cats hunt rats again" became "Cats not stopped from hunting" and the toll gate claim was dropped (neither the gate nor a cat hunt has been seen in game); rows are now 41 px apart so 14 fit the panel, checked with a local render.

| File | Derived from |
|---|---|
| `header.jpg`, `thumbnail.jpg` | `up-key.png`, copy of `Screenshot From 2026-11-16 03-16-42.png`; Bazaar square, the casino front with Bar Bizarre and Hotel Korsc behind, HUD hidden |
| `gallery-01.jpg` | `up-shelf.png`, copy of `Screenshot From 2026-11-15 21-50-25.png` |
| `gallery-02.jpg` | `up-coffee.png`, harness capture 2026-11-19 (headless, UI scale 1, full UI); the Fridge panel at the Open Sewer Tenement market moonshine stall with a Coffee at 100%, and the tooltip on its twin in the inventory (95%) showing "Should be refrigerated or frozen" |
| `gallery-03.jpg` | `fixes-card.svg`; rendered by render.sh, no screenshot |
| all | house-style kit templates (`art/templates/`), fonts and textures listed in `art/SOURCES.md` |

## In-game UI

- The mod draws no UI of its own. The only in-game text is the "Account frozen" notification, shown through the game's own notification panel.
