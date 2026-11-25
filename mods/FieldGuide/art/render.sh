#!/usr/bin/env bash
# Renders the Field Guide Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing; PARTIAL=1 skips any missing shot. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/FieldGuide}
OUT=${OUT:-mods/FieldGuide/art/nexus}
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

shot fg-mash.png "Opening the mash tun shows a short guide to mashing."
shot fg-sauna.png "The sauna stove gets a guide to heat and steam."
shot fg-tooltip.png "Tooltips list who buys an item and what each pays."
shot fg-panel.png "The item panel in the inventory lists buyers too."

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Field Guide" "How stations work, and who buys what." --key-shot "$(key fg-key.png)" --out "$OUT" "${args[@]}"
