#!/usr/bin/env bash
# Renders the Fewer Clicks Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing; PARTIAL=1 skips any missing shot. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/FewerClicks}
OUT=${OUT:-mods/FewerClicks/art/nexus}
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

shot fc-topup-before.png "Before: the chest holds 4 Junk Wood, and you carry 6." optional
shot fc-topup.png "After Top up: the chest stack is full, and 2 stay with you."
shot fc-swing.png "Hold the button on a tree or rock and the tool keeps swinging."
shot fc-quickeat.png "Press 5 to eat and drink, and a notification says what you had."
shot fc-fillall.png "One click fills every container you carry, one after another."

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Fewer Clicks" "Fewer clicks for the things you do all day." --key-shot "$(key fc-key.png)" --out "$OUT" "${args[@]}"
