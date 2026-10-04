# Animated Album Art for Jellyfin

Animated Album Art plays looping video artwork over the static cover on Jellyfin Web album detail pages. Put a `cover-motion.mp4` or `cover-motion.mov` file in an album folder; the plugin discovers it and serves it through authenticated server endpoints.

The static cover stays visible while the video loads, when playback fails, and when the viewer has enabled reduced motion. Videos play muted, loop automatically, and stop when their album page is hidden or closed. The plugin injects its bundled script into the server's web index response without changing Jellyfin Web files on disk.

## Compatibility and scope

- This project targets **.NET 10** and references **Jellyfin 12.1.0**. Its package ABI is `12.1.0.0`. Use a compatible server; older Jellyfin versions are not targeted by this build.
- The built-in presentation applies to **album detail pages in Jellyfin Web**. Other clients can use the API below to implement their own presentation. Album grids and now-playing screens are not animated by this script.
- Albums must have a local filesystem directory accessible to the Jellyfin server. Discovery checks that directory, without searching subdirectories.
- Videos are served unchanged. The plugin does not download, generate, convert, or transcode artwork. Playback depends on the browser's codec support.
- The web script downloads the complete video before playing it, so short, compact files work best. The server API also supports byte-range streaming.

## Build and install

Install the .NET 10 SDK, then run from the repository root:

```sh
dotnet build Jellyfin.Plugin.AnimatedAlbumArt.slnx -c Release
dotnet test Jellyfin.Plugin.AnimatedAlbumArt.slnx -c Release
```

Stop Jellyfin, create an `AnimatedAlbumArt_1.0.0.0` subdirectory in **your server's plugin directory**, and copy this file into it:

```text
Jellyfin.Plugin.AnimatedAlbumArt/bin/Release/net10.0/Jellyfin.Plugin.AnimatedAlbumArt.dll
```

Start Jellyfin again and confirm **Animated Album Art** appears under Dashboard → Plugins. Reload Jellyfin Web so the client script loads. Use the plugin directory for your installation rather than assuming a platform-specific path. For the Docker development server in this repository, it is `dev/data/config/plugins/AnimatedAlbumArt_1.0.0.0/` on the host.

Only the plugin DLL is needed for installation. Jellyfin supplies the framework and server dependencies; their runtime assets are excluded from the plugin project. Copy the matching PDB alongside the DLL when debugging.

### Install from a release repository

The release workflow produces a plugin ZIP and a Jellyfin `manifest.json` release asset. Once a stable release has been published and its workflow has completed, add this repository URL under Dashboard → Plugins → Repositories:

```text
https://github.com/jellyfin-ai-labs/animated-album-artwork/releases/latest/download/manifest.json
```

Then install Animated Album Art from the catalog and restart Jellyfin. For a specific release, including a prerelease, use `/releases/download/<tag>/manifest.json` instead of `/releases/latest/download/manifest.json`. Each manifest describes the single release packaged by that workflow; the latest URL follows GitHub's latest stable release.

**No release is published merely by building locally.** Until release assets exist, use the manual installation above. The manifest format follows Jellyfin's [plugin repository documentation](https://jellyfin.org/posts/plugin-updates/).

## Add artwork

Keep the normal static cover and place the video next to the album's tracks:

```text
Music/
└── Artist/
    └── Album/
        ├── 01 - Track.mp3
        ├── cover.jpg
        └── cover-motion.mp4
```

Names are matched case-insensitively. By default, `cover-motion.mp4` wins over `cover-motion.mov` if both exist. You can enable `.m4v` and `.webm` in settings as well. An extension must be both supported by the plugin and enabled in the configured list.

The filesystem is checked on each API lookup. Adding, replacing, or deleting a sidecar needs no library scan for an already indexed album. The web client caches artwork information while viewing a page: navigate away and back, or reload, to pick up a change. Keep `cover.jpg` for the static fallback.

## Settings

Open Dashboard → Plugins → Animated Album Art to change these settings:

| Setting | Configuration property | Default | Behavior |
| --- | --- | --- | --- |
| Serve motion artwork | `Enabled` | `true` | When off, album info reports no artwork and video/diagnostics requests return `404`. |
| Allowed extensions | `AllowedExtensions` | `.mp4,.mov` | Comma-separated priority order. Supported values: `.mp4`, `.m4v`, `.mov`, `.webm`; unsupported entries are ignored. |
| Show motion artwork in Jellyfin Web | `InjectWebClient` | `true` | Injects the client script into the server-hosted web index. Reload the browser after changing it. |

Disabling web injection leaves the album API available. Injection applies to Jellyfin Web served by this server; a separately hosted web client must load the script itself.

## Server API

These are all the routes added by this plugin. Prefix paths with the Jellyfin server URL and any configured base path, for example `https://media.example.com/jellyfin/AnimatedAlbumArt/...`.

| Method | Path | Access | Result |
| --- | --- | --- | --- |
| `GET` | `/AnimatedAlbumArt/Albums/{albumId}` | Authenticated user with album access | JSON describing available motion artwork. |
| `GET`, `HEAD` | `/AnimatedAlbumArt/Albums/{albumId}/Video` | Authenticated user with album access | Original video bytes, or headers only for `HEAD`. |
| `GET` | `/AnimatedAlbumArt/Albums/{albumId}/Diagnostics` | Administrator with album access | JSON containing the file path and advisory format checks. |
| `GET` | `/AnimatedAlbumArt/ClientScript` | Public; no authentication | Bundled Jellyfin Web JavaScript. |

`albumId` is the Jellyfin **MusicAlbum GUID**, not a track ID or a filesystem path. Find album IDs through Jellyfin's existing `GET /Items?Recursive=true&IncludeItemTypes=MusicAlbum` endpoint. Album endpoints apply the caller's library visibility and parental controls. A missing, inaccessible, or non-album item returns `404`; malformed GUIDs return `400`.

Use a Jellyfin user access token in the header:

```text
Authorization: MediaBrowser Token="<user-access-token>"
```

Missing or invalid credentials on protected endpoints return `401`. The server also accepts `?ApiKey=<user-access-token>` for the video route, but header authentication avoids putting credentials in URLs and logs. The bundled client uses an authentication header and plays the downloaded video through a blob URL.

### GET album artwork information

```sh
curl -H "Authorization: MediaBrowser Token=\"$JELLYFIN_TOKEN\"" \
  "$JELLYFIN_URL/AnimatedAlbumArt/Albums/$ALBUM_ID"
```

Example `200` response (tag and size are illustrative):

```json
{
  "AlbumId": "11111111-2222-3333-4444-555555555555",
  "HasMotionArt": true,
  "ContentType": "video/mp4",
  "Size": 1048576,
  "Tag": "100000-8de000000000000"
}
```

`Size` is the file size in bytes. `Tag` identifies the file using its size and modification time and can be used to invalidate a client cache. For a visible album with no enabled sidecar, the response is still `200`, with `HasMotionArt: false` and `ContentType`, `Size`, and `Tag` set to `null` (or omitted if the server is configured to omit null fields).

### GET or HEAD video

```sh
curl -H "Authorization: MediaBrowser Token=\"$JELLYFIN_TOKEN\"" \
  -H 'Range: bytes=0-1023' \
  "$JELLYFIN_URL/AnimatedAlbumArt/Albums/$ALBUM_ID/Video" \
  --output artwork-part.bin
```

The response content type is `video/mp4` for `.mp4`/`.m4v`, `video/quicktime` for `.mov`, or `video/webm` for `.webm`. Responses include `ETag`, `Last-Modified`, and `Cache-Control: private, no-cache`. Use `If-None-Match` or `If-Modified-Since` to revalidate cached artwork.

| Status | Meaning |
| --- | --- |
| `200` | Complete file; `HEAD` returns its headers without a body. |
| `206` | Requested byte range, with `Content-Range`. |
| `304` | Cached file is unchanged according to a conditional request. |
| `404` | No enabled sidecar, or the album is unavailable to the caller. |
| `416` | Requested byte range cannot be satisfied. |

The web client adds a `tag` query parameter for cache identification. The server does not require or validate this parameter; it always finds the current file and emits the current ETag.

### GET diagnostics

```sh
curl -H "Authorization: MediaBrowser Token=\"$JELLYFIN_TOKEN\"" \
  "$JELLYFIN_URL/AnimatedAlbumArt/Albums/$ALBUM_ID/Diagnostics"
```

This endpoint requires an administrator token. An authenticated non-administrator receives `403`. A successful `200` response contains:

```json
{
  "AlbumId": "11111111-2222-3333-4444-555555555555",
  "Path": "/media/music/Artist/Album/cover-motion.mp4",
  "AppleMotionChecks": [
    {
      "Name": "Video codec",
      "Passed": true,
      "Expected": "H.264 or Apple ProRes",
      "Actual": "h264"
    }
  ]
}
```

The full check list covers container, video codec, dimensions, duration, frame rate, audio, bit rate, pixel aspect ratio, and color primaries. `Passed` is `true`, `false`, or `null` when the probe lacks the needed information; `Actual` may also be null. These are advisory checks implemented by this plugin, not a guarantee of Apple delivery acceptance or browser compatibility. Files that fail checks are still served.

Diagnostics run the Jellyfin server's configured `ffprobe`, with a 30-second timeout. A missing probe executable, an undecodable file, or a probe failure can cause a server error rather than a diagnostic response. This endpoint exposes a server filesystem path and is restricted to administrators.

### GET client script

`GET /AnimatedAlbumArt/ClientScript` returns `200` with `Content-Type: application/javascript; charset=utf-8` and `Cache-Control: no-cache`. It is public so a script tag can load it before login. Album and video requests made by the script still require the signed-in user's token.

With `InjectWebClient` enabled, middleware adds a script tag to Jellyfin Web's index HTML at `/web/` and `/web/index.html` (including a server base path). These are existing Jellyfin routes, not additional plugin endpoints. Other web assets are left untouched.

## Development

Read [.agents/AGENTS.md](.agents/AGENTS.md) before changing the plugin.

The local development environment requires Docker with Compose, .NET 10, `ffmpeg`, `curl`, and `jq`:

```sh
dev/make-test-library.sh   # generate six sample albums
dev/run.sh                # build, install, start/restart, and configure Jellyfin
dev/test-server.sh        # run the server integration checks
```

Open `http://localhost:8096`. The development accounts are `admin` / `admin` and `limited` / `limited`; the limited user has no library access. This setup is for a local test server. Generated media, configuration, and tokens live in ignored `dev/data/`.

`dev/run.sh --fresh` deletes the local development server data and regenerates its library. Use it only when you want to reset this test instance. The Compose file uses `jellyfin/jellyfin:latest`; check that its server version remains compatible with the package references when pulling a newer image.

The integration script checks discovery, extension priority, streaming and caching, authorization, diagnostics, live file changes, configuration, and web script injection. It temporarily modifies the generated library and plugin settings. Browser playback, reduced-motion behavior, and static fallback should also be checked on the album detail pages when changing the client script.

### Debugging

VS Code's `build-and-copy` task publishes the plugin in Debug mode and stages its DLL and PDB in the configured plugin directory. Defaults point to the Docker development data directory. Restart the development server after staging a new build.

For **Launch**, build matching Jellyfin server and web source checkouts first, then set `jellyfinDir`, `jellyfinWebDir`, and `jellyfinDataDir` in `.vscode/settings.json` to their actual paths. The launch configuration starts `bin/Debug/net10.0/jellyfin.dll` with the configured web and data directories. Stop any server using the same data directory or port before launching. Those source checkouts are prerequisites, not included in this repository.

**Attach to local Jellyfin** lets you select a running native .NET server process. Attaching to the Docker container requires a separate container debugger setup; the local attach configuration does not provide it.

### Packaging and releases

`build.yaml` is the package metadata, including the stable plugin GUID, version, framework, target ABI, and artifact list. Keep the default assembly version in `Directory.Build.props` consistent with that metadata.

For a local release package and manifest:

```sh
python3 -m venv /tmp/animated-album-art-packaging
/tmp/animated-album-art-packaging/bin/pip install 'jprm==1.1.0'
mkdir -p artifacts
/tmp/animated-album-art-packaging/bin/jprm plugin build . --dotnet-framework net10.0
/tmp/animated-album-art-packaging/bin/jprm repo init artifacts/manifest.json
/tmp/animated-album-art-packaging/bin/jprm repo add \
  --plugin-url 'https://github.com/jellyfin-ai-labs/animated-album-artwork/releases/download/v1.0.0.0/animated-album-art_1.0.0.0.zip' \
  artifacts/manifest.json artifacts/*.zip
```

Use a new or empty `artifacts/` directory for each packaging run. The generated manifest includes the ZIP URL, checksum, plugin identity, version, and target ABI. Local generation prepares assets; it does not upload them.

Publishing a GitHub release with a tag such as `v1.0.0.0` runs `.github/workflows/publish.yaml`: it checks out the tag, runs unit tests, packages that version, generates a manifest, and uploads both assets to that release using the repository's built-in token. Manual workflow dispatch accepts an existing release tag. Release tags must contain four numeric components with an optional `v` prefix. This workflow does not require Jellyfin's deployment secrets.

## License

[GNU General Public License v3.0](LICENSE).
