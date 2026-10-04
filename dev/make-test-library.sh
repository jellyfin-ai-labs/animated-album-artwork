#!/usr/bin/env bash
# Generates a small music library for testing the plugin (requires ffmpeg).
#
#   Motion Album/   -> cover.jpg + cover-motion.mp4 (H.264, silent, square)
#   MOV Album/      -> cover.jpg + cover-motion.mov
#   Both Album/     -> both cover-motion.mp4 and cover-motion.mov (.mp4 should win)
#   Static Album/   -> cover.jpg only
#   Broken Album/   -> cover.jpg + an undecodable cover-motion.mp4 (static cover is the fallback)
#   Motion Only Album/ -> cover-motion.mp4 with no static cover
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)/data/media/music/Test Artist"
rm -rf "$root"
mkdir -p "$root"

tone() { # dir, track number, title, album
  ffmpeg -loglevel error -y -f lavfi -i "sine=frequency=$((300 + $2 * 110)):duration=20" \
    -metadata title="$3" -metadata album="$4" -metadata artist="Test Artist" \
    -metadata album_artist="Test Artist" -metadata track="$2" -c:a libmp3lame -q:a 6 \
    "$1/0$2 - $3.mp3"
}

cover() { # dir, color
  ffmpeg -loglevel error -y -f lavfi -i "color=c=$2:s=600x600" -frames:v 1 "$1/cover.jpg"
}

motion() { # output file, extra encoder args...
  local out="$1"; shift
  ffmpeg -loglevel error -y -f lavfi -i "testsrc2=s=720x720:r=30:d=8" -an -pix_fmt yuv420p "$@" "$out"
}

for album in "Motion Album" "MOV Album" "Both Album" "Static Album" "Broken Album" "Motion Only Album"; do
  dir="$root/$album"
  mkdir -p "$dir"
  tone "$dir" 1 "First Song" "$album"
  tone "$dir" 2 "Second Song" "$album"
done

cover "$root/Motion Album" teal
cover "$root/MOV Album" purple
cover "$root/Both Album" orange
cover "$root/Static Album" gray
cover "$root/Broken Album" maroon

motion "$root/Motion Album/cover-motion.mp4" -c:v libx264 -movflags +faststart
motion "$root/MOV Album/cover-motion.mov" -c:v libx264
motion "$root/Both Album/cover-motion.mp4" -c:v libx264 -movflags +faststart
motion "$root/Both Album/cover-motion.mov" -c:v libx264
motion "$root/Motion Only Album/cover-motion.mp4" -c:v libx264 -movflags +faststart
head -c 65536 /dev/urandom > "$root/Broken Album/cover-motion.mp4"

echo "Test library written to $root"
