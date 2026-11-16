#!/usr/bin/env bash
# Renders the No Floor Spill Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/NoFloorSpill}
OUT=${OUT:-mods/NoFloorSpill/art/nexus}
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

shot nfs-before.png "Before: once the ash slot is full, ash piles up on the floor."
shot nfs-after.png "After: the extra ash is deleted, and the floor stays clean."
shot nfs-chest.png "Or the extra ash goes into a chest you own nearby."
shot nfs-leftover.png "Leftovers go into your inventory while the machine is open."

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "No Floor Spill" "Ash and leftovers stay off the floor." --key-shot "$(path nfs-after.png)" --out "$OUT" "${args[@]}"
