# Local playback-copy validation

Tested using the local Docker Jellyfin server, the configured Jellyfin FFmpeg, and the collaborative Chromium browser. This is development code, not a published release or a test of the user's original HEVC artwork.

The large synthetic fixture is the same 720×720, 30 fps, 26-second H.264 test video used in `performance-v0.1.1.md`, containing 161,881,998 bytes. The playback profile uses libx264, CRF 23, a 4 Mbps VBV ceiling, no audio, at most 720 pixels per edge, at most 30 fps, and MP4 faststart.

| Metric | Original | Generated copy |
| --- | ---: | ---: |
| File size | 161,881,998 bytes | 5,822,628 bytes |
| Average encoded bitrate (size × 8 / duration) | 49.81 Mbps | 1.79 Mbps |
| Duration | 26 seconds | 26 seconds |

The copy is **96.40% smaller**. One background generation run, including frame-count checks and decode validation, completed in **4.63 seconds** on this development machine. Generation time depends on server hardware and source complexity.

For the browser check, video fetches used `cache: 'no-store'`. A test wrapper delayed each consumed response body by its byte length divided by 1,875,000 bytes/second to simulate an additional 15 Mbps delivery constraint. This is a synthetic application-level delay, not network shaping or a measured real-server bandwidth result.

- First playback: **637.5 ms**, after **1,048,576 bytes** were consumed.
- Three native loops completed; each restarted after approximately **0.6 seconds** of refill waiting. This implementation is not gapless.
- Playback buffers were evicted (observed range approximately 2–18 seconds at time 7 seconds in the second loop).
- All video responses were authenticated `206` ranges with no credentials in URLs; no media errors or unhandled rejections were observed.
- Navigation aborted and removed playback and cleared the blob URL. A broken source retained the static cover.

Real-encoder integration checks also verified all 192 frames across four independently initialized two-second HEVC MP4 sections with timestamps starting at ten seconds survive conversion into an eight-second copy; 1280×960/60 fps input with audio becomes a silent 720×540/30 fps copy; original revisions remain pinned during publication; replacing an original invalidates cached selection; anonymous and inaccessible-album requests remain denied; settings and the scheduled preparation task work; non-square source pixels retain their display aspect ratio.

Reproduce with `dev/run.sh`, `dev/test-server.sh`, and `python3 dev/test-playback-cache.py`. The integration script generates a large synthetic fixture if one is not already available, restores source files and settings, and leaves only disposable cache entries. Visual quality of the user's real artwork and generation time on the NAS remain unmeasured.
