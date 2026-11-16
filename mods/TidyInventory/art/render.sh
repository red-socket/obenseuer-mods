#!/usr/bin/env bash
# Renders the Tidy Inventory Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing; PARTIAL=1 skips any missing shot. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/TidyInventory}
OUT=${OUT:-mods/TidyInventory/art/nexus}
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


shot ti-pin.png "Middle-click a slot to pin it, and Sort leaves it alone."
shot ti-ghost.png "An empty pinned slot keeps a faded picture of its item."
shot ti-empty.png "Click Empty twice to clear a trash bin."
shot ti-combine-before.png "Before: three part-used packs of painkillers."
shot ti-combine.png "After Combine: full packs and one part-used pack."
shot ti-duck.png "Once Mr. Duck's quest is done, the blue duck can go." optional

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Tidy Inventory" "Keep your inventory where you put it." --key-shot "$(key ti-key.png)" --out "$OUT" "${args[@]}"
