# Obenseuer mods

Quality-of-life mods and unofficial fixes for [Obenseuer](https://store.steampowered.com/app/951240/Obenseuer/) by Loiste Interactive, built on BepInEx 5 and HarmonyX. Releases go up on [Nexus Mods](https://www.nexusmods.com/obeseuer).

Each mod does only what its description says. Its settings live in the BepInEx config, and you can remove it without breaking your save.

## Mods

| Mod | Status | What it does |
|---|---|---|
| [Save Guard](mods/SaveGuard) | In development | Timed autosaves, rotating backups, crash-safe save writes and a backup before each game update. |
| [Better Tenement](mods/BetterTenement) | In development | Contractors use stolen materials first, resource chests hold twice as much, slow renovations finish, and a ledger shows what upgrades earn. A doctor window finds the junk that keeps a flat from counting as cleared. |
| [Fair Justice](mods/FairJustice) | In development | Moving stolen items between your inventory, backpack and equipment is no longer a new theft, and neither is using soap, disinfectant or a magazine where it lies. On arrest, items that might not fit your locker go in the orange bag. In jail you see how long you're in for, and on release you can walk out without being arrested again. |
| [Unofficial Patch](mods/UnofficialPatch) | In development | Small fixes for bugs still in 0.5.19, each with its own switch: biweekly rent, scam payouts into a frozen account, console keys leaking into the game, sprint and crouch toggles, stuck liquid residue, ice cream and coffee spoilage, the Rexus tablet shelf, cats that never hunt, police walking through closed doors and porta-potty doors that shut in your face. |
| [No Floor Spill](mods/NoFloorSpill) | In development | Ash from a full stove is deleted or put in your own storage nearby instead of spilling onto the floor. Leftovers from a full machine, like plastic trash and empty bottles, go to your inventory or nearby storage. |
| [Fewer Clicks](mods/FewerClicks) | In development | A Top up button moves items from your inventory onto stacks a storage already has, and Fill all fills every container you carry at a sink, tap or barrel. Hold the use button on a tree or rock and the tool keeps swinging, and pressing 5 eats and drinks what you need. |
| [Time Keeper](mods/TimeKeeper) | In development | Every watch you wear shows the day of the week, and a key opens the sleep clock so you can pass time awake. Waiting costs what the same time costs anywhere else. |
| [Safe Travels](mods/SafeTravels) | In development | The toll bridge stops charging twice, a pause menu button gets you unstuck, and the minitrain stops coupling and uncoupling on its own. A key puts a derailed locomotive back on its rails. |

## Installing

1. Install [BepInEx 5.4.23.6](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6) (`BepInEx_win_x64`) into the game folder.
2. On Linux or Steam Deck (Proton), set the Steam launch option `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
3. Extract a mod's release archive into the game folder, so its DLL ends up in `BepInEx/plugins/<ModName>/`.

## Building

You need the .NET SDK (any version that can target `net472`) and an installed copy of the game, because the projects reference the game's own assemblies.

```bash
cd mods/SaveGuard
dotnet build -c Release
```

The build looks for the game in the default Steam library under `~/.local/share/Steam`. Point it elsewhere with `-p:GameDir=/path/to/Obenseuer`. Add `-p:Deploy=true` to copy the built plugin into the game's `BepInEx/plugins/` folder.

## Layout

- `Directory.Build.props` holds the build settings and game references shared by every mod.
- `mods/<ModName>/` has one folder per mod, with its source, `CHANGELOG.md` and `art/`.
- `art/` is the shared house style for Nexus images: templates, the renderer, and where each game asset it uses comes from.

## License

[GPL-3.0](LICENSE). Obenseuer and its assets belong to Loiste Interactive. This project is not affiliated with or endorsed by them, and the repository contains no game files.
