# Message Log art

Nothing in this folder ships: the mod draws no art of its own. The window is IMGUI in the house palette, and its only text inside the game's own UI is the Notes page hint.

| Use | Game asset | Treatment |
|---|---|---|
| Message Log window | none (IMGUI) | House `Skin`: panel #1f1f1f, rows #2b2b2b, selected row and active chip #835213, amber #ffab00, copied from Fair Market (Quest Helper's). |
| Category colours (row heads) | none | Crime #e06666, Money #ffab00, Mail #6fa8dc, Quests #93c47d, Items #d9d9d9 (only if D7 splits them), Dialogue #c3a6e0, Other #9e9e9e. The category name is always printed too. |
| Notes page hint "F)MESSAGES" / "X)MESSAGES" | the journal's control hint line (`InventoryNavigationHandler.GetButtonInfo`, TMP) | Appended in the game's own "KEY)LABEL" style. |
| Pause menu entry "Message Log" (optional) | a clone of the pause menu's Resume button | Label only; the button's look is the game's. |

## Nexus images

Not made yet. When they are, follow the house kit (`art/STYLE.md` and `art/SOURCES.md` at the repo root) and Fair Market's `art/render.sh` pattern, and record each screenshot and crop here. Proposed shot list (captions to go through the humanizer):

| # | Screenshot | Set up | Caption |
|---|---|---|---|
| 01 | `ml-window.png` | A save with a day of play: Tab, Notes page, F. All chip, a few money, mail and quest rows. | Every notification, with the day and time. |
| 02 | `ml-dialogue.png` | After a conversation with a named NPC: Dialogue chip. | What people said, and what you answered. |
| 03 | `ml-search.png` | Keyboard: type a word that matches a mail and a task. | Search the whole log. |
| 04 | `ml-notes-hint.png` | The Notes page hint line with F)MESSAGES next to R)INVOICES (with Fair Market). | Opens from the journal's Notes page. |

| File | Derived from |
|---|---|
| (screenshots: filled in after the final render) | |
