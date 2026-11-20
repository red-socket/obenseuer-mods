# Changelog

All notable changes to Display Options are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Frame cap with VSync off: the game's own cap (the default), your display's real refresh rate, a number you choose, or no cap.
- Shadow budget, off by default. The Light, Balanced and Strong presets, or your own values, remove shadows from small props, turn off lamp shadows past a set distance, and lower the sun's shadow distance and cascades. They can only lower the game's settings.
- Switches for six camera effects: motion blur, chromatic aberration, grain, vignette, depth of field and lens distortion. All of them keep the game's look by default. The game mostly uses these effects when you're drunk, on mushrooms, in the Wasteland, in the main menu and during some status effects.
- UI scale from 75% to 150% for the HUD, the game menu, conversations, shops, the card game and the loading screen. Sizes that would push the game menu off the screen, or leave the speaker's name too little room in a conversation, are limited to the largest size that fits. An optional setting keeps conversations at their 16:9 size on ultrawide screens, and another lets the pause menu follow the scale. The default is 100%, which changes nothing.
- An optional "UI Scale" slider in Pause > Options > Quality, off by default.

### Fixed

- With VSync on, the game also capped the frame rate at your refresh rate setting (or 60). In borderless and windowed mode, the two limits together could lock the game at half your refresh rate. Display Options leaves the pacing to VSync, with only a loose limit of twice your refresh rate (at least 240 FPS) in case VSync doesn't hold. Exclusive fullscreen doesn't change. The fix is on by default, and you can turn it off.
