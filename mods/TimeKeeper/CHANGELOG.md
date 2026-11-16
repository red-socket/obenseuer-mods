# Changelog

All notable changes to Time Keeper are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [semantic versioning](https://semver.org/).

## [0.1.0] - Unreleased

Tested on Obenseuer 0.5.19 with BepInEx 5.4.23.6.

### Added

- First release.
- Weekday on watches: every watch you wear shows the day of the week next to the time, not only the four digital ones. Wall clocks don't change, and you still need a watch to see the time. The Scope setting can also add the weekday for a watch (AnyWatchCarried) or an alarm clock (AnyClock) in your pockets while the inventory is open.
- Pass time awake: press Home (D-pad Up on a controller) to open the game's sleep clock as a waiting clock. Pick a duration in 15-minute steps, up to 12 hours, and press Home again (or the Wait button, or A) to start. Press Home, Esc, B or D-pad Up to stop early. Waiting costs what the same time costs anywhere else: hunger, thirst, tiredness, drug and alcohol timers, spoilage, rent and world events carry on, and nothing rests or heals you the way sleep does. Needs tick once per in-game minute, as when you stand still, whatever your frame rate. The screen goes black and the world goes quiet, as when you watch TV.
- You can't start waiting in menus, dialogue, shops and trades, while building, swimming, climbing a ladder or riding the minitrain, when the police are after you, a robber is chasing you or you were hurt in the last 20 seconds, in jail, and when hunger, thirst, health, mental health, tiredness or alcohol (counting drinks that haven't kicked in yet) would end the wait at once. A wait stops early when a need gets too high, tiredness goes over 90, you're about to pass out drunk, you get hurt, or someone talks to you. If you pass out anyway, it runs its full course, as it would without the mod. On the minitrain the D-pad stays the throttle.
- Settings: the keys and controller buttons, the longest wait (MaxHours), the step size (StepMinutes), the starting duration (DefaultMinutes), the speed (SpeedFactor), optional RequireSeated and RequireClock, AllowInJail, the danger timer (DangerSeconds), StopWhenHurt and the notes (Notify).
