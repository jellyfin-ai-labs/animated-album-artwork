#!/usr/bin/env bash
# End-to-end tests against the dev server. Run dev/run.sh first.
set -uo pipefail

base="${JELLYFIN_URL:-http://localhost:8096}"
here="$(cd "$(dirname "$0")" && pwd)"
music="$here/data/media/music/Test Artist"
plugin_id="0c8d1d43-0ad6-4d51-b5ac-b39b6f46fbcb"
admin=(-H "Authorization: MediaBrowser Token=\"$(cat "$here/data/admin-token")\"")
limited=(-H "Authorization: MediaBrowser Token=\"$(cat "$here/data/limited-token")\"")
failures=0

check() { # description, expected, actual
  if [ "$2" = "$3" ]; then
    printf '  ok    %s\n' "$1"
  else
    printf '  FAIL  %s (expected %q, got %q)\n' "$1" "$2" "$3"
    failures=$((failures + 1))
  fi
}
status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
header() { curl -s -o /dev/null -D - "${@:2}" | tr -d '\r' | awk -v h="$1" 'tolower($0) ~ "^"tolower(h)": " { sub(/^[^:]*: /, ""); print }'; }
info() { curl -s "${admin[@]}" "$base/AnimatedAlbumArt/Albums/$1" | jq -r "$2"; }
album_id() { curl -s "${admin[@]}" "$base/Items?Recursive=true&IncludeItemTypes=MusicAlbum" | jq -r --arg n "$1" '.Items[] | select(.Name==$n) | .Id'; }
set_config() { # jq filter applied to the plugin configuration
  local config
  config="$(curl -s "${admin[@]}" "$base/Plugins/$plugin_id/Configuration" | jq "$1")"
  curl -s -o /dev/null "${admin[@]}" -H 'Content-Type: application/json' -X POST \
    "$base/Plugins/$plugin_id/Configuration" -d "$config"
}

original_config="$(curl -s "${admin[@]}" "$base/Plugins/$plugin_id/Configuration")"
restore_config() {
  curl -s -o /dev/null "${admin[@]}" -H 'Content-Type: application/json' -X POST \
    "$base/Plugins/$plugin_id/Configuration" -d "$original_config"
}
trap restore_config EXIT
# Discovery and byte identity checks below intentionally exercise originals.
set_config '.GeneratePlaybackCopies = false'

motion="$(album_id "Motion Album")"
mov="$(album_id "MOV Album")"
both="$(album_id "Both Album")"
static="$(album_id "Static Album")"
track="$(curl -s "${admin[@]}" "$base/Items?Recursive=true&IncludeItemTypes=Audio&Limit=1" | jq -r '.Items[0].Id')"
video="$base/AnimatedAlbumArt/Albums/$motion/Video"

echo "Discovery"
check "mp4 sidecar found" "true video/mp4" "$(info "$motion" '"\(.HasMotionArt) \(.ContentType)"')"
check "mov sidecar found" "true video/quicktime" "$(info "$mov" '"\(.HasMotionArt) \(.ContentType)"')"
check "mp4 preferred over mov" "video/mp4" "$(info "$both" .ContentType)"
check "album without sidecar" "false" "$(info "$static" .HasMotionArt)"
check "static album has no video" "404" "$(status "${admin[@]}" "$base/AnimatedAlbumArt/Albums/$static/Video")"
check "music library does not import sidecars as items" "0" "$(curl -s "${admin[@]}" "$base/Items?Recursive=true&IncludeItemTypes=Video,Movie,MusicVideo" | jq .TotalRecordCount)"

echo "Streaming"
check "full download matches file" "$(shasum -a 256 < "$music/Motion Album/cover-motion.mp4")" "$(curl -s "${admin[@]}" "$video" | shasum -a 256)"
check "content type" "video/mp4" "$(header Content-Type "${admin[@]}" "$video")"
check "mov content type" "video/quicktime" "$(header Content-Type "${admin[@]}" "$base/AnimatedAlbumArt/Albums/$mov/Video")"
check "range request" "206" "$(status "${admin[@]}" -H 'Range: bytes=100-199' "$video")"
check "range bytes match file" "$(tail -c +101 "$music/Motion Album/cover-motion.mp4" | head -c 100 | shasum -a 256)" "$(curl -s "${admin[@]}" -H 'Range: bytes=100-199' "$video" | shasum -a 256)"
check "content range" "bytes 100-199/$(wc -c < "$music/Motion Album/cover-motion.mp4" | tr -d ' ')" "$(header Content-Range "${admin[@]}" -H 'Range: bytes=100-199' "$video")"
check "unsatisfiable range" "416" "$(status "${admin[@]}" -H 'Range: bytes=999999999-' "$video")"
etag="$(header ETag "${admin[@]}" "$video")"
check "conditional request" "304" "$(status "${admin[@]}" -H "If-None-Match: $etag" "$video")"
check "HEAD" "200" "$(status -I "${admin[@]}" "$video")"
check "ApiKey query parameter" "200" "$(status "$video?ApiKey=$(cat "$here/data/admin-token")")"

echo "Access control"
check "anonymous info" "401" "$(status "$base/AnimatedAlbumArt/Albums/$motion")"
check "anonymous video" "401" "$(status "$video")"
check "user without library access: info" "404" "$(status "${limited[@]}" "$base/AnimatedAlbumArt/Albums/$motion")"
check "user without library access: video" "404" "$(status "${limited[@]}" "$video")"
check "unknown id" "404" "$(status "${admin[@]}" "$base/AnimatedAlbumArt/Albums/00000000000000000000000000000001/Video")"
check "non-album id" "404" "$(status "${admin[@]}" "$base/AnimatedAlbumArt/Albums/$track")"
check "path-like id rejected" "400" "$(status "${admin[@]}" "$base/AnimatedAlbumArt/Albums/..%2F..%2Fetc%2Fpasswd/Video")"
check "diagnostics: admin" "200" "$(status "${admin[@]}" "$base/AnimatedAlbumArt/Albums/$motion/Diagnostics")"
check "diagnostics: non-admin" "403" "$(status "${limited[@]}" "$base/AnimatedAlbumArt/Albums/$motion/Diagnostics")"

echo "Diagnostics"
diag="$(curl -s "${admin[@]}" "$base/AnimatedAlbumArt/Albums/$motion/Diagnostics")"
check "codec passes" "true" "$(jq -r '.AppleMotionChecks[] | select(.Name=="Video codec") | .Passed' <<<"$diag")"
check "720x720 fails dimensions" "false 720x720" "$(jq -r '.AppleMotionChecks[] | select(.Name=="Dimensions") | "\(.Passed) \(.Actual)"' <<<"$diag")"
check "no audio passes" "true" "$(jq -r '.AppleMotionChecks[] | select(.Name=="Audio") | .Passed' <<<"$diag")"
check "frame rate" "true 30" "$(jq -r '.AppleMotionChecks[] | select(.Name=="Frame rate") | "\(.Passed) \(.Actual)"' <<<"$diag")"

echo "Live file changes (no rescan)"
cp "$music/Motion Album/cover-motion.mp4" "$music/Static Album/cover-motion.mp4"
check "added sidecar is served" "true" "$(info "$static" .HasMotionArt)"
old_tag="$(info "$static" .Tag)"
sleep 1
ffmpeg -loglevel error -y -f lavfi -i "testsrc2=s=360x360:r=25:d=3" -an -pix_fmt yuv420p -c:v libx264 "$music/Static Album/cover-motion.mp4"
check "replaced sidecar changes tag" "true" "$([ "$(info "$static" .Tag)" != "$old_tag" ] && echo true || echo false)"
check "replaced sidecar invalidates old ETag" "200" "$(status "${admin[@]}" -H "If-None-Match: \"$old_tag\"" "$base/AnimatedAlbumArt/Albums/$static/Video")"
mv "$music/Static Album/cover-motion.mp4" "$music/Static Album/COVER-MOTION.MP4"
check "name matching is case-insensitive" "true" "$(info "$static" .HasMotionArt)"
rm "$music/Static Album/COVER-MOTION.MP4"
check "deleted sidecar is gone" "false" "$(info "$static" .HasMotionArt)"
check "deleted sidecar video" "404" "$(status "${admin[@]}" "$base/AnimatedAlbumArt/Albums/$static/Video")"

echo "Configuration"
set_config '.AllowedExtensions = ".mov,.mp4"'
check "extension priority is configurable" "video/quicktime" "$(info "$both" .ContentType)"
set_config '.AllowedExtensions = ".mp4"'
check "disallowed extension is ignored" "false" "$(info "$mov" .HasMotionArt)"
set_config '.AllowedExtensions = ".mp4,.mov" | .Enabled = false'
check "disabled plugin reports nothing" "false" "$(info "$motion" .HasMotionArt)"
check "disabled plugin serves nothing" "404" "$(status "${admin[@]}" "$video")"
set_config '.Enabled = true'

echo "Jellyfin Web integration"
check "client script is public" "200" "$(status "$base/AnimatedAlbumArt/ClientScript")"
check "client script type" "application/javascript; charset=utf-8" "$(header Content-Type "$base/AnimatedAlbumArt/ClientScript")"
check "script injected into /web/" "1" "$(curl -s "$base/web/" | grep -c 'data-animated-album-art')"
check "script injected into /web/index.html" "1" "$(curl -s "$base/web/index.html" | grep -c 'data-animated-album-art')"
check "injected despite gzip request" "1" "$(curl -s --compressed "$base/web/" | grep -c 'data-animated-album-art')"
check "injected despite cached copy" "200" "$(status -H 'If-Modified-Since: Sun, 01 Jan 2040 00:00:00 GMT' "$base/web/index.html")"
check "other web files untouched" "0" "$(curl -s "$base/web/manifest.json" | grep -c 'data-animated-album-art')"
set_config '.InjectWebClient = false'
check "injection can be turned off" "0" "$(curl -s "$base/web/" | grep -c 'data-animated-album-art')"
set_config '.InjectWebClient = true'

echo
if [ "$failures" -eq 0 ]; then echo "All checks passed"; else echo "$failures check(s) failed"; exit 1; fi
