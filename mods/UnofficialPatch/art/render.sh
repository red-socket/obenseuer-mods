#!/usr/bin/env bash
# Renders the Unofficial Patch Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/UnofficialPatch}
OUT=${OUT:-mods/UnofficialPatch/art/nexus}
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

# The fixes card is ours, not a screenshot: render it from fixes-card.svg (librsvg only loads images
# beside the SVG, so stage it with the grain texture; the game fonts come from AssetRipper).
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
cp mods/UnofficialPatch/art/fixes-card.svg "$tmp/"; cp art/textures/concrete.png "$tmp/"
printf '<?xml version="1.0"?><!DOCTYPE fontconfig SYSTEM "fonts.dtd"><fontconfig><include ignore_missing="yes">/etc/fonts/fonts.conf</include><dir>%s</dir><cachedir>%s</cachedir></fontconfig>' \
  "$PWD/AssetRipper/ExportedAssets/Assets/Font" "$tmp/fc-cache" > "$tmp/fonts.conf"
FONTCONFIG_FILE=$tmp/fonts.conf rsvg-convert "$tmp/fixes-card.svg" -o "$tmp/fixes-card.png"

shot up-frozen.png "A scam payout into a frozen account stays in that account."
shot up-shelf.png "The Rexus tablet shelf restocks again."
shot up-still.png "A still stuck with 0.1 L of liquid can be drained again." optional
shot up-coffee.png "Coffee keeps in the fridge, and ice cream in the freezer."
shot up-cat.png "Cats hunt rats again." optional
args+=(--shot "$tmp/fixes-card.png" "Every fix has its own switch.")

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Unofficial Patch" "Small fixes for confirmed bugs." --key-shot "$(path up-shelf.png)" --out "$OUT" "${args[@]}"
