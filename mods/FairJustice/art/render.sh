#!/usr/bin/env bash
# Renders the Fair Justice Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/FairJustice}
OUT=${OUT:-mods/FairJustice/art/nexus}
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

shot fj-panel.png "A panel shows when you get out and what you are in for."
shot fj-bags.png "Moving stolen items between your own bags is no longer theft."
shot fj-bag.png "What doesn't fit the cell locker goes into the orange bag."
shot fj-locker.png "Items you were allowed to have stay in your locker." optional

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Fair Justice" "Crime still doesn't pay, but you only pay for it once." --key-shot "$(path fj-panel.png)" --out "$OUT" "${args[@]}"
