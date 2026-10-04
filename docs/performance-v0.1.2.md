# v0.1.2 fragmented artwork browser validation

Tested the streaming fix temporarily in the collaborative Chromium browser against the user's Jellyfin server over HTTP. The deployed v0.1.1 bundle was tested first, then the local fixed client was injected for validation. The browser was reloaded afterward to restore the deployed client.

The real artwork file was a **167,576,288-byte fragmented HEVC MP4**, codec `hvc1.2.4.H150.b0`, supported by this browser's MediaSource implementation. v0.1.1 excluded fragmented MP4 from streaming and fell back to a full-file blob download after a 1 MiB probe.

| Metric | Deployed v0.1.1 | Fixed client |
| --- | ---: | ---: |
| Bytes consumed before playback | 168,624,864 | 5,242,880 |
| Startup request behavior | 1 MiB range probe, then full download | 1 MiB probe, then 4 MiB range |
| Startup byte reduction | — | 96.89% |

Cache and network conditions were uncontrolled, so this check does not establish a time-to-playback improvement. The earlier controlled H.264 benchmark in `performance-v0.1.1.md` used a regular MP4 and does not represent this fragmented file.

The fixed client played through the entire file and continued through a subsequent loop, with old buffered frames evicted and no media errors. The file contains four repeated initialization sections with timestamps starting at ten seconds. Sequence-mode MSE normalizes those sections into 64 seconds of playback; the full-file blob path reported 26 seconds. This is a behavior difference for this unusual input.

All playback requests used authenticated `206` range responses without API keys in URLs. An unauthenticated range request returned `401`. Authentication headers avoid credential-bearing playback URLs; they do not encrypt plain HTTP transport.

These measurements concern startup bytes, not total bandwidth or process memory. Playback preserves the original bitrate, and subsequent loops fetch evicted frames again. Unsupported browsers/codecs retain the complete-file compatibility fallback.
