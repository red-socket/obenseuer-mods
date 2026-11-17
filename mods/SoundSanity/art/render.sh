#!/usr/bin/env bash
# Renders the Sound Sanity Nexus images (header, thumbnail, gallery) into nexus/ with the house-style kit.
# Screenshots are not kept in git: they live in $SHOTS, named as in NOTES.md. Optional shots are
# skipped when missing; PARTIAL=1 skips any missing shot. PLACEHOLDER=1 stands the game's loading-screen render in for missing shots and
# OUT=dir renders elsewhere (both for checking text fit before the screenshots exist).
set -euo pipefail
cd "$(dirname "$0")/../../.."
SHOTS=${SHOTS:-$HOME/Pictures/obenseuer-nexus/SoundSanity}
OUT=${OUT:-mods/SoundSanity/art/nexus}
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

# The settings card is ours, not a screenshot: render it from settings-card.svg (librsvg only loads images
# beside the SVG, so stage it with the grain texture; the game fonts come from AssetRipper).
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
cp mods/SoundSanity/art/settings-card.svg "$tmp/"; cp art/textures/concrete.png "$tmp/"
printf '<?xml version="1.0"?><!DOCTYPE fontconfig SYSTEM "fonts.dtd"><fontconfig><include ignore_missing="yes">/etc/fonts/fonts.conf</include><dir>%s</dir><cachedir>%s</cachedir></fontconfig>' \
  "$PWD/AssetRipper/ExportedAssets/Assets/Font" "$tmp/fc-cache" > "$tmp/fonts.conf"
FONTCONFIG_FILE=$tmp/fonts.conf rsvg-convert "$tmp/settings-card.svg" -o "$tmp/settings-card.png"

shot ss-sliders.png "The volume sliders now reach sounds that ignored them."
shot ss-machines.png "Crushers and saws can be turned down on their own."
shot ss-radio.png "Radio music follows the Music slider and can be muted."
args+=(--shot "$tmp/settings-card.png" "Each machine group has its own volume, from 0 to 100%.")

rm -f "$OUT"/gallery-*.jpg
uv run art/render.py "Sound Sanity" "When you turn it down, it goes down." --key-shot "$(key ss-key.png)" --out "$OUT" "${args[@]}"
