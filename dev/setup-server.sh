#!/usr/bin/env bash
# Completes the first-run wizard on a fresh dev server and creates:
#   admin / admin       - administrator
#   limited / limited   - user with no library access (for access-control tests)
#   "Music" library     - /media/music
# Prints the admin access token to dev/data/admin-token. Safe to re-run.
set -euo pipefail

base="${JELLYFIN_URL:-http://localhost:8096}"
here="$(cd "$(dirname "$0")" && pwd)"
client='MediaBrowser Client="dev-setup", Device="cli", DeviceId="dev-setup", Version="1.0"'

until curl -sf "$base/System/Info/Public" >/dev/null; do sleep 1; done

if [ "$(curl -s "$base/System/Info/Public" | jq -r .StartupWizardCompleted)" != "true" ]; then
  until curl -sf "$base/Startup/User" >/dev/null; do sleep 1; done
  curl -sf -X POST "$base/Startup/Configuration" -H 'Content-Type: application/json' \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
  curl -sf -X POST "$base/Startup/User" -H 'Content-Type: application/json' \
    -d '{"Name":"admin","Password":"admin"}'
  curl -sf -X POST "$base/Startup/Complete"
fi

login() {
  curl -sf -X POST "$base/Users/AuthenticateByName" -H 'Content-Type: application/json' \
    -H "Authorization: $client" -d "{\"Username\":\"$1\",\"Pw\":\"$2\"}" | jq -r .AccessToken
}

token="$(login admin admin)"
auth=(-H "Authorization: $client, Token=\"$token\"")
echo "$token" > "$here/data/admin-token"

if ! curl -sf "${auth[@]}" "$base/Library/VirtualFolders" | jq -e '.[] | select(.Name=="Music")' >/dev/null; then
  curl -sf -X POST "${auth[@]}" -H 'Content-Type: application/json' \
    "$base/Library/VirtualFolders?name=Music&collectionType=music&refreshLibrary=true" \
    -d '{"LibraryOptions":{"PathInfos":[{"Path":"/media/music"}]}}'
  # The scan started by refreshLibrary can run before the path is saved; scan again.
  curl -sf -X POST "${auth[@]}" "$base/Library/Refresh"
fi

until [ "$(curl -sf "${auth[@]}" "$base/ScheduledTasks" | jq -r '.[] | select(.Key=="RefreshLibrary") | .State')" = Idle ]; do
  sleep 1
done

if ! curl -sf "${auth[@]}" "$base/Users" | jq -e '.[] | select(.Name=="limited")' >/dev/null; then
  id="$(curl -sf -X POST "${auth[@]}" -H 'Content-Type: application/json' "$base/Users/New" \
    -d '{"Name":"limited","Password":"limited"}' | jq -r .Id)"
  policy="$(curl -sf "${auth[@]}" "$base/Users/$id" | jq '.Policy | .EnableAllFolders=false | .EnabledFolders=[]')"
  curl -sf -X POST "${auth[@]}" -H 'Content-Type: application/json' "$base/Users/$id/Policy" -d "$policy"
fi
login limited limited > "$here/data/limited-token"

echo "Server ready at $base (admin/admin, limited/limited)"
