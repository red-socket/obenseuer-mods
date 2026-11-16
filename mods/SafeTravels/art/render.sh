#!/usr/bin/env bash
# Renders the Safe Travels Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing; PARTIAL=1 skips any missing shot. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/SafeTravels}
OUT=${OUT:-mods/SafeTravels/art/nexus}
PLACEHOLDER_SHOT=AssetRipper/ExportedProject/Assets/Texture2D/LoadingScreenBg_weird_people.png
args=()
path() { # file -> path, or the placeholder
  if [[ -f $SHOTS/$1 ]]; then echo "$SHOTS/$1"
  elif [[ -n ${PLACEHOLDER:-} ]]; then echo "$PLACEHOLDER_SHOT"; fi
}
shot() { # file caption [optional]
  local p; p=$(path "$1")
  if [[ -n $p ]]; then args+=(--shot "$p" "$2")
  elif [[ ${3:-} == optional || -n ${PARTIAL:-} ]]; then echo "skipping optional $1"
  else echo "missing $SHOTS/$1" >&2; exit 1; fi
}
key() { # the header and thumbnail shot (a HUD-free gameplay shot), or the first gallery shot while it is missing
  local p; p=$(path "$1"); echo "${p:-${args[1]}}"
}


shot st-toll-paid.png "Once you've paid, the toll slot takes no more coins."
shot st-unstuck-menu.png "Get Unstuck in the pause menu takes you back a short way."
shot st-minitrain.png "Your wagons stay coupled through switches and curves."
shot st-rerail.png "Press End to put a derailed locomotive back on its rails." optional

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Safe Travels" "Fewer ways to get stuck, robbed or left behind." --key-shot "$(key st-key.png)" --out "$OUT" "${args[@]}"
