# Changelog

All notable changes to Better Placement are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Align to nearby furniture (#5614, #6492): in building mode, hold Left Alt (or D-pad left) and the piece you're placing lines up with furniture close by. It snaps to centres, matching edges and flush sides, on the floor and on walls, and thin guide lines show which line it took. When aligning starts, it also turns the piece once to match a neighbour that's within 10° of it, unless you've already turned the piece yourself. Toggle and Always modes are in the config. Aligning only moves the piece to spots the game already allows, and a placement waits a frame or two until the game has checked the snapped spot.
- Rotation snapping (#6208): while rotating a piece, hold Mouse 4 to turn it in 15° steps or Mouse 5 for 45° steps, whatever rotation mode you're in. On a controller, hold D-pad right while pulling a trigger. Let go and you're back in your own mode. The controls line shows the held step.
- Remembered angles: each kind of furniture or item starts at the angle you last placed it at. By default this lasts until you quit the game and writes nothing to your save; it can be kept in the save instead.
- Controller drift fix: a trigger that doesn't rest at zero no longer spins furniture in building mode or hides the mouse cursor while you're on mouse and keyboard. A real pull still rotates, and a light one still turns the piece slowly.
- Rugs under furniture and curtains behind it (#5848), off by default: rugs can go under tables, chairs and beds, and curtains can hang behind shelves, where the game used to say the spot was taken. Walls, doors, other rugs, flat things and items you can pick up still block rugs, and a curtain still can't cover anything you can use.
- Nothing the mod does goes into your save unless you choose to keep angles there, so you can add or remove it at any point in a playthrough.
