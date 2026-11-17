#!/usr/bin/env bash
# Renders the Time Keeper Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing; PARTIAL=1 skips any missing shot. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/TimeKeeper}
OUT=${OUT:-mods/TimeKeeper/art/nexus}
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


shot tk-weekday.png "Every watch you wear shows the day of the week."
shot tk-wait.png "Press Home to pass time awake on the game's sleep clock."
shot tk-blocked.png "If you can't wait right now, a short note says why." optional

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Time Keeper" "See the day on any watch, and wait without a bed." --key-shot "$(key tk-key.png)" --header-shot "$(key tk-key-header.png)" --out "$OUT" "${args[@]}"
