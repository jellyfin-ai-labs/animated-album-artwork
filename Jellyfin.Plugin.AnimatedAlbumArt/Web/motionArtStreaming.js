import { createFile } from 'mp4box';

const CHUNK_SIZE = 1024 * 1024;
const FRAGMENT_CHUNK_SIZE = 4 * CHUNK_SIZE;
const BUFFER_AHEAD = 8;
const BUFFER_BEHIND = 5;

class UnsupportedStreaming extends Error {}

// All waits are cancellable, including waits for playback/looping after EOF.
function waitFor(target, events, signal) {
    signal.throwIfAborted();
    return new Promise((resolve, reject) => {
        function cleanup() {
            events.forEach(event => target.removeEventListener(event, done));
            signal.removeEventListener('abort', abort);
        }
        function done(event) {
            cleanup();
            if (event.type === 'error') {
                reject(new Error('Motion artwork media error.'));
            } else {
                resolve();
            }
        }
        function abort() {
            cleanup();
            reject(signal.reason);
        }
        events.forEach(event => target.addEventListener(event, done));
        signal.addEventListener('abort', abort, { once: true });
    });
}

async function updateBuffer(buffer, action, signal) {
    // Install listeners before the operation so even synchronous failures clean up.
    const controller = new AbortController();
    const operationSignal = AbortSignal.any([signal, controller.signal]);
    const finished = waitFor(buffer, ['updateend', 'error'], operationSignal);
    try {
        action();
        await finished;
    } catch (error) {
        controller.abort();
        await finished.catch(() => {});
        throw error;
    }
}

function bufferedAhead(video) {
    for (let i = 0; i < video.buffered.length; i++) {
        if (video.buffered.start(i) <= video.currentTime + 0.1 && video.buffered.end(i) > video.currentTime) {
            return video.buffered.end(i) - video.currentTime;
        }
    }
    return 0;
}

// A normal <video src> cannot set an Authorization header. Fetch byte ranges,
// feed existing fragments to MSE, and remux ordinary MP4 locally. No key, grant,
// or cookie is placed in a URL, and this works on HTTP private-network installs.
export async function streamMotionArt(video, url, token, info, signal) {
    const MediaSourceClass = globalThis.MediaSource;
    if (!MediaSourceClass || !AbortSignal.any ||
        !['video/mp4', 'video/quicktime'].includes(info.ContentType) ||
        !Number.isSafeInteger(info.Size) || info.Size <= 0) {
        return false;
    }

    let mediaSource;
    let sourceBuffer;
    let seekTarget = null;
    let etag = info.Tag ? '"' + info.Tag + '"' : null;
    const onSeek = () => { seekTarget = video.currentTime; };

    async function readRange(offset, length = CHUNK_SIZE) {
        const end = Math.min(offset + length, info.Size) - 1;
        const headers = {
            Authorization: 'MediaBrowser Token="' + token + '"',
            Range: 'bytes=' + offset + '-' + end
        };
        if (etag) {
            headers['If-Range'] = etag;
        }
        const response = await fetch(url, { headers, signal });
        if (response.status === 200) {
            // A proxy may ignore ranges, or the sidecar may have changed. Do not
            // accidentally download the full file here or mix file revisions.
            await response.body?.cancel();
            throw new UnsupportedStreaming('Byte ranges unavailable.');
        }
        if (response.status !== 206 ||
            response.headers.get('Content-Range') !== 'bytes ' + offset + '-' + end + '/' + info.Size ||
            (etag && response.headers.get('ETag') !== etag)) {
            await response.body?.cancel();
            throw new Error('Invalid motion artwork range response: ' + response.status);
        }
        etag = response.headers.get('ETag');
        const data = await response.arrayBuffer();
        if (data.byteLength !== end - offset + 1) {
            throw new Error('Incomplete motion artwork range.');
        }
        data.fileStart = offset;
        return data;
    }

    try {
        let target = 0;
        while (!signal.aborted) {
            // A new parser for seeks/loops releases sample tables and all old
            // sample data. The MSE buffer keeps only a moving playback window.
            const file = createFile(true);
            const queue = [];
            const prefix = [];
            let metadata;
            let parserError;
            let complete = false;
            let offset = 0;
            file.onReady = value => { metadata = value; };
            file.onError = error => { parserError = new Error('Invalid motion artwork: ' + error); };

            while (!metadata && offset < info.Size) {
                const data = await readRange(offset);
                prefix.push(data);
                const next = file.appendBuffer(data);
                if (parserError) {
                    throw parserError;
                }
                // MP4Box skips mdat to reach moov when metadata is at the end.
                offset = Math.max(next ?? 0, offset + data.byteLength);
            }
            const track = metadata?.videoTracks[0];
            const fragmented = metadata?.isFragmented;
            // Existing fragments retain their original tracks, including muted
            // audio. A SourceBuffer must declare every codec in that container.
            const codecs = fragmented ? metadata.tracks.map(item => item.codec).join(',') : track?.codec;
            const mime = 'video/mp4; codecs="' + codecs + '"';
            if (!track || (!fragmented && !track.nb_samples) || !MediaSourceClass.isTypeSupported(mime)) {
                throw new UnsupportedStreaming('Artwork is not supported by MSE.');
            }
            if (fragmented) {
                offset = 0;
                for (const data of prefix) {
                    if (data.fileStart !== offset) {
                        throw new UnsupportedStreaming('Fragment initialization is not at the beginning.');
                    }
                    offset += data.byteLength;
                }
                // Unlike normal MP4 sample tables, fragment timestamps live in
                // the first moof. Read through it before normalizing the timeline.
                while (!file.getTrackSamplesInfo(track.id)[0] && offset < info.Size) {
                    const data = await readRange(offset);
                    prefix.push(data);
                    file.appendBuffer(data);
                    offset += data.byteLength;
                }
            }

            if (!mediaSource) {
                mediaSource = new MediaSourceClass();
                const opened = waitFor(mediaSource, ['sourceopen'], signal);
                video.src = URL.createObjectURL(mediaSource);
                await opened;
                sourceBuffer = mediaSource.addSourceBuffer(mime);
                // Empty-moov fragmented files often omit the duration. MSE
                // determines their actual duration at endOfStream instead.
                mediaSource.duration = track.duration > 0 ? track.duration / track.timescale : Infinity;
                video.addEventListener('seeking', onSeek);
            }
            if (fragmented) {
                const firstSample = file.getTrackSamplesInfo(track.id)[0];
                if (!firstSample) {
                    throw new Error('Fragmented artwork contains no video samples.');
                }
                // Sequence mode normalizes nonzero timestamps and discontinuities
                // between independently initialized fragments. Reset the group
                // start on every loop so refill begins at zero again.
                sourceBuffer.mode = 'sequence';
                sourceBuffer.timestampOffset = 0;
            }

            if (fragmented) {
                // Fragmented MP4 is already an MSE byte stream. Append it in
                // order, including partial moof/mdat boxes across range chunks.
                queue.push(...prefix);
                complete = offset === info.Size;
            } else {
                file.onSegment = (id, user, data, sampleNumber, last) => {
                    queue.push(data);
                    file.releaseUsedSamples(id, sampleNumber);
                    complete = last;
                };
                // Small segments start quickly even if the original GOP is long.
                // Only the video track is selected; motion artwork is always silent.
                file.setSegmentOptions(track.id, null, { nbSamples: 30, rapAlignement: false });
                queue.push(file.initializeSegmentation('per-track')[0].buffer);
                offset = file.seek(target, true).offset;
                file.start();
            }

            while (seekTarget === null) {
                while (queue.length && seekTarget === null) {
                    const removeBefore = Math.max(0, video.currentTime - BUFFER_BEHIND);
                    if (sourceBuffer.buffered.length && sourceBuffer.buffered.start(0) < removeBefore) {
                        await updateBuffer(sourceBuffer, () => sourceBuffer.remove(0, removeBefore), signal);
                    }
                    const data = queue.shift();
                    await updateBuffer(sourceBuffer, () => sourceBuffer.appendBuffer(data), signal);
                }
                if (seekTarget !== null) {
                    break;
                }
                if (complete) {
                    mediaSource.endOfStream();
                    // Native looping seeks to zero, even when old frames have
                    // been evicted. Refill those frames using authenticated ranges.
                    await waitFor(video, ['seeking', 'error'], signal);
                    break;
                }
                while (bufferedAhead(video) > BUFFER_AHEAD && seekTarget === null) {
                    await waitFor(video, ['timeupdate', 'seeking', 'error'], signal);
                }
                if (seekTarget !== null) {
                    break;
                }
                if (!Number.isSafeInteger(offset) || offset < 0 || offset >= info.Size) {
                    throw new Error('Motion artwork ended before all frames were received.');
                }
                const data = await readRange(offset, fragmented ? FRAGMENT_CHUNK_SIZE : CHUNK_SIZE);
                if (fragmented) {
                    queue.push(data);
                    offset += data.byteLength;
                    complete = offset === info.Size;
                } else {
                    const next = file.appendBuffer(data);
                    if (parserError) {
                        throw parserError;
                    }
                    if (!complete && (!Number.isSafeInteger(next) || next <= offset)) {
                        throw new Error('Motion artwork parser made no progress.');
                    }
                    offset = next;
                }
            }
            file.stop();
            target = seekTarget ?? 0;
            seekTarget = null;
            // After looping, the old tail would otherwise count as "ahead" of
            // zero and accumulate alongside the refilled beginning of the file.
            if (sourceBuffer.buffered.length) {
                await updateBuffer(sourceBuffer, () => sourceBuffer.remove(0, mediaSource.duration), signal);
            }
            // Seeks may interrupt a partial input fragment. Reset the native
            // parser before appending a fresh initialization segment on a refill.
            sourceBuffer.abort();
        }
        signal.throwIfAborted();
    } catch (error) {
        if (error instanceof UnsupportedStreaming && !mediaSource) {
            return false;
        }
        throw error;
    } finally {
        video.removeEventListener('seeking', onSeek);
    }
}

// Compatibility fallback for WebM, unsupported MSE codecs,
// and browsers without MSE. The original header-authenticated blob path remains.
export async function loadMotionArt(video, url, token, info, signal) {
    if (await streamMotionArt(video, url, token, info, signal)) {
        return;
    }
    signal.throwIfAborted();
    const response = await fetch(url, {
        headers: { Authorization: 'MediaBrowser Token="' + token + '"' },
        signal
    });
    if (!response.ok) {
        throw new Error('Motion artwork request failed: ' + response.status);
    }
    const blob = await response.blob();
    signal.throwIfAborted();
    video.src = URL.createObjectURL(blob);
}
