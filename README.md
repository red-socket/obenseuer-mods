# Obenseuer mods

Quality-of-life mods and unofficial fixes for [Obenseuer](https://store.steampowered.com/app/951240/Obenseuer/) by Loiste Interactive, built on BepInEx 5 and HarmonyX. Releases are published on [Nexus Mods](https://www.nexusmods.com/obeseuer).

Each mod is small, does one job, is configurable through the BepInEx config, and can be removed without breaking a save.

## Mods

| Mod | Status | What it does |
|---|---|---|
| [Save Guard](mods/SaveGuard) | In development | Timed autosaves, rotating backups, crash-safe save writes and a backup before each game update. |

## Installing

1. Install [BepInEx 5.4.23.6](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6) (`BepInEx_win_x64`) into the game folder.
2. On Linux/Steam Deck (Proton), set the Steam launch option `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
3. Extract a mod's release archive into the game folder, so its DLL ends up in `BepInEx/plugins/<ModName>/`.

## Building

Requires the .NET SDK (any version that can target `net472`) and a local install of the game; the projects reference the game's own assemblies.

```bash
cd mods/SaveGuard
dotnet build -c Release
```

The game path defaults to the Steam library in `~/.local/share/Steam`. Override it with `-p:GameDir=/path/to/Obenseuer`. Add `-p:Deploy=true` to copy the built plugin into the game's `BepInEx/plugins/` folder.

## Layout

- `Directory.Build.props`: shared build settings and game references for every mod.
- `mods/<ModName>/`: one folder per mod, with its source, `CHANGELOG.md` and `art/`.
- `art/`: the shared house style for Nexus images (templates, renderer and provenance of every game asset used).

## License

[GPL-3.0](LICENSE). Obenseuer and its assets belong to Loiste Interactive; this project is not affiliated with or endorsed by them. No game files are included in this repository.
