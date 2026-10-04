# Working on Animated Album Art

## Start here

Read `README.md` for supported behavior, setup, and the complete endpoint contract. This file is stored in `.agents/` as requested; agents working elsewhere in the repository should load it explicitly if their runner does not discover it automatically.

## Boundaries

- `MotionArt/` owns filesystem sidecar discovery, extension priority, MIME types, and file tags. Check files on each lookup so changes need no rescan; restrict discovery to the album directory.
- `Api/` owns authenticated album lookup and HTTP responses. Resolve albums through the caller's Jellyfin user and check visibility before returning metadata or bytes. Diagnostics require the elevation policy; only the client script is public.
- `Diagnostics/` owns ffprobe execution and advisory profile checks. Keep probe arguments in `ProcessStartInfo.ArgumentList`, honor cancellation, and retain a bounded timeout. Profile failures must not block video serving.
- `Web/` owns response-time script injection and album detail playback. Preserve the static fallback, reduced-motion preference, authenticated header-based downloads, and disposal of hidden videos and blob URLs. The browser downloads complete files, so account for memory and bandwidth when changing playback.
- `Configuration/` owns persisted options and the embedded dashboard page. Keep its property names and defaults aligned with the C# configuration and README.

## Change and verify

1. Check `git status` and preserve unrelated work. Follow `.editorconfig` and the analyzers configured in the project; warnings fail the plugin build.
2. Keep the plugin GUID stable and aligned between `Plugin.cs`, `configPage.html`, and `build.yaml`. For compatibility changes, align Jellyfin package references, the target framework, and package target ABI. Keep release metadata and default assembly versions consistent; release packaging overrides the version from its tag.
3. Run `dotnet test Jellyfin.Plugin.AnimatedAlbumArt.slnx -c Release` after C# changes. Add regression coverage for changed discovery, injection, or profile behavior where existing tests do not cover it.
4. For server integration changes, build and install with `dev/run.sh`, then run `dev/test-server.sh`. These checks mutate the disposable test library and settings. Use `--fresh` only when a reset of `dev/data/` is intended.
5. For web changes, verify playback on an album detail page, navigation away, reduced motion, and a broken-video static fallback. Server integration tests verify script delivery and injection, not browser playback.
6. For packaging changes, build a ZIP and manifest with the README's JPRM commands and inspect their identity, version, ABI, artifact contents, URL, and checksum. State whether a workflow was only checked locally or actually executed on GitHub.
7. Update the README's API documentation whenever routes, authentication, response fields, or status codes change. Finish with the changes made, checks run, and any unverified runtime prerequisites.

## Local environment

`dev/data/`, `bin/`, `obj/`, and `artifacts/` are generated and ignored. Development tokens stay in `dev/data/`; keep them out of commits and tool output. `dev/setup-server.sh` creates deliberately simple local test credentials; use it for the development instance.

The VS Code launch configuration requires matching prebuilt Jellyfin server and web checkouts. Configure their paths before claiming launch debugging has been verified. The local attach configuration targets native processes, not Docker.
