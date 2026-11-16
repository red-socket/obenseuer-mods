#!/usr/bin/env bash
# Renders the Better Tenement Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/BetterTenement}
OUT=${OUT:-mods/BetterTenement/art/nexus}
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

shot bt-chest.png "Resource chests hold 176 stacks and get a Stash button."
shot bt-card.png "Upgrade cards show what each tier adds to the rent."
shot bt-ledger.png "The Ledger shows rent, renovations and a shopping list."
shot bt-doctor.png "The Doctor shows what keeps a flat from counting as cleared."
shot bt-stolen.png "Contractors use your stolen materials first."

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Better Tenement" "Landlording without the bookkeeping." --key-shot "$(path bt-doctor.png)" --out "$OUT" "${args[@]}"
