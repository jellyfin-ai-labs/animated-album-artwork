#!/usr/bin/env bash
# Builds the plugin, installs it into the dev server, and (re)starts the server.
#   dev/run.sh            build + install + restart
#   dev/run.sh --fresh    also wipe server data, regenerate the test library and rerun setup
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(dirname "$here")"
compose=(docker compose -f "$here/docker-compose.yml")

if [ "${1:-}" = "--fresh" ]; then
  "${compose[@]}" down
  rm -rf "$here/data"
  "$here/make-test-library.sh"
fi

dotnet build "$repo/Jellyfin.Plugin.AnimatedAlbumArt" -c Release -o "$here/data/build" --nologo -v quiet
# Keep a single development install so old version folders cannot mask a build.
plugin_dir="$here/data/config/plugins/AnimatedAlbumArt_Dev"
for old_plugin in "$here"/data/config/plugins/AnimatedAlbumArt_[0-9]*; do
  if [ -d "$old_plugin" ]; then rm -rf "$old_plugin"; fi
done
mkdir -p "$plugin_dir"
cp "$here/data/build/Jellyfin.Plugin.AnimatedAlbumArt.dll" "$plugin_dir/"

"${compose[@]}" up -d
if docker inspect -f '{{.State.Running}}' animated-album-art-jellyfin >/dev/null 2>&1; then
  "${compose[@]}" restart
fi
"$here/setup-server.sh"
