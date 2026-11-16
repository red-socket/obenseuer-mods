#!/usr/bin/env bash
# Renders the Save Guard Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/SaveGuard}
OUT=${OUT:-mods/SaveGuard/art/nexus}
PLACEHOLDER_SHOT=AssetRipper/ExportedProject/Assets/Texture2D/LoadingScreenBg_weird_people.png
args=()
path() { # file -> path, or the placeholder
  if [[ -f $SHOTS/$1 ]]; then echo "$SHOTS/$1"
  elif [[ -n ${PLACEHOLDER:-} ]]; then echo "$PLACEHOLDER_SHOT"; fi
}
shot() { # file caption [optional]
  local p; p=$(path "$1")
  if [[ -n $p ]]; then args+=(--shot "$p" "$2")
  elif [[ ${3:-} == optional ]]; then echo "skipping optional $1"
  else echo "missing $SHOTS/$1" >&2; exit 1; fi
}

shot sg-window.png "Press F6 in the main menu to see every backup of a character."
shot sg-confirm.png "Save Guard keeps the slot you replace, so you can undo a restore."
shot sg-damaged.png "Save Guard asks before you load a damaged save." optional
shot sg-ingame.png "In game, F6 shows how many backups you have."
shot sg-notice.png "Save Guard backs up every save before a new game version runs." optional

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Save Guard" "Backups and crash-safe saves." --key-shot "$(path sg-window.png)" --out "$OUT" "${args[@]}"
