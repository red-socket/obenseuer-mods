# Obenseuer mods

Quality-of-life mods and unofficial fixes for [Obenseuer](https://store.steampowered.com/app/951240/Obenseuer/) by Loiste Interactive, built on BepInEx 5 and HarmonyX. Releases go up on [Nexus Mods](https://www.nexusmods.com/obeseuer).

Every mod does one thing. Its settings live in the BepInEx config, and you can remove it without breaking your save.

## Mods

| Mod | Status | What it does |
|---|---|---|
| [Save Guard](mods/SaveGuard) | In development | Timed autosaves, rotating backups, crash-safe save writes and a backup before each game update. |
| [Better Tenement](mods/BetterTenement) | In development | Contractors use stolen materials first, resource chests hold twice as much, slow renovations finish, and a ledger shows what upgrades earn. A doctor window finds the junk that keeps a flat from counting as cleared. |

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
