# v0.1.1 playback measurements

Measured on October 3, 2026 using the collaborative Chromium browser and the local Docker Jellyfin server over HTTP. Three runs per path, alternating the complete-file blob fallback and MSE streaming, with browser HTTP caching disabled (`fetch` with `cache: 'no-store'`) for both. The server filesystem cache was warm. The same 26-second H.264 MP4 was used in every run: **161,881,998 bytes**, 720×720, approximately 49.8 Mbps, metadata at the end.

Time to playback is measured from the first artwork video request to the video's first `playing` event. It excludes album metadata and page loading. Downloaded bytes are response-body bytes consumed before that event, including repeated ranges and the metadata lookup. These are startup measurements, not a claim that the complete loop transfers fewer bytes.

| Metric | Complete-file download | MSE streaming | Improvement |
| --- | ---: | ---: | ---: |
| Median time to playback | 941.0 ms | 143.4 ms | 6.56× faster; 84.76% less time |
| Bytes downloaded before playback | 161,881,998 | 7,344,712 | 95.46% fewer |
| Buffered video at startup | 26 seconds | Approximately 1 second | Playback starts before the whole video is buffered |
| Requests started before playback | 1 | 9 | More small requests replace one blocking download |

Raw startup times (ms):

- Complete file: 941.0, 857.6, 943.5.
- Streaming: 143.4, 157.0, 135.1.

All video requests used an authorization header and contained no credential in their URLs. Browser checks also verified MP4 and MOV playback, looping after eviction, clearing the old tail on a loop, request cancellation and blob URL disposal on navigation/reduced motion, broken-file static fallback, and successful playback with MediaSource disabled.

The generated fixture can be reproduced with:

```sh
ffmpeg -loglevel error -y -f lavfi -i testsrc2=s=720x720:r=30:d=26 \
  -an -pix_fmt yuv420p -c:v libx264 -preset ultrafast \
  -b:v 50M -minrate 50M -maxrate 50M -bufsize 50M \
  -x264-params nal-hrd=cbr:force-cfr=1 -g 30 cover-motion.mp4
```

Results vary with browser, codec, keyframe spacing, network speed, and cache state. Process RAM was not measured; bounded playback buffering is not equivalent to a measured RAM reduction. Streaming preserves the original bitrate and quality. A complete loop still downloads its video frames, and evicted frames are fetched again on later loops.
