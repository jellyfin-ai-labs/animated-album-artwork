import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test, afterEach } from 'node:test';
import { loadMotionArt, streamMotionArt } from '../Jellyfin.Plugin.AnimatedAlbumArt/Web/motionArtStreaming.js';

const originalFetch = globalThis.fetch;
const originalMediaSource = globalThis.MediaSource;
const originalCreateObjectURL = URL.createObjectURL;
afterEach(() => {
    globalThis.fetch = originalFetch;
    globalThis.MediaSource = originalMediaSource;
    URL.createObjectURL = originalCreateObjectURL;
});

const emptyRanges = { length: 0 };
class Video extends EventTarget {
    currentTime = 0;
    buffered = emptyRanges;
}

function installMediaSource(onEnd, appended = [], removed = []) {
    class SourceBuffer extends EventTarget {
        buffered = emptyRanges;
        appendBuffer(data) {
            appended.push(new Uint8Array(data));
            queueMicrotask(() => this.dispatchEvent(new Event('updateend')));
        }
        remove(start, end) {
            removed.push([start, end]);
            this.buffered = emptyRanges;
            queueMicrotask(() => this.dispatchEvent(new Event('updateend')));
        }
    }
    class MediaSource extends EventTarget {
        static isTypeSupported(mime) { return mime.includes('avc1.'); }
        addSourceBuffer(mime) {
            assert.match(mime, /^video\/mp4; codecs="avc1\./);
            this.buffer = new SourceBuffer();
            return this.buffer;
        }
        endOfStream() { onEnd(this); }
    }
    globalThis.MediaSource = MediaSource;
    URL.createObjectURL = object => {
        if (object instanceof MediaSource) {
            queueMicrotask(() => object.dispatchEvent(new Event('sourceopen')));
        }
        return 'blob:local-artwork';
    };
}

function serveRanges(bytes, requests) {
    globalThis.fetch = async (url, options) => {
        assert.equal(url, 'http://jellyfin/Video?tag=test');
        assert.equal(options.headers.Authorization, 'MediaBrowser Token="secret"');
        assert.equal(options.headers['If-Range'], '"test"');
        const [, start, end] = /^bytes=(\d+)-(\d+)$/.exec(options.headers.Range).map(Number);
        requests.push([start, end]);
        return new Response(bytes.subarray(start, end + 1), {
            status: 206,
            headers: { 'Content-Range': `bytes ${start}-${end}/${bytes.length}`, ETag: '"test"' }
        });
    };
}

const url = 'http://jellyfin/Video?tag=test';
const info = size => ({ Size: size, ContentType: 'video/mp4', Tag: 'test' });

test('remuxes real MP4 samples into MSE segments and cancels the EOF wait', async () => {
    const bytes = await readFile(new URL('fixtures/faststart.mp4', import.meta.url));
    const requests = [];
    const segments = [];
    const controller = new AbortController();
    serveRanges(bytes, requests);
    installMediaSource(() => queueMicrotask(() => controller.abort()), segments);
    await assert.rejects(streamMotionArt(new Video(), url, 'secret', info(bytes.length), controller.signal), { name: 'AbortError' });
    assert.ok(segments.length >= 3, 'init plus incremental video segments');
    assert.ok(segments.some(data => Buffer.from(data).includes(Buffer.from('moof'))));
    assert.equal(requests.length, 1);
});

test('jumps over large media/padding to metadata at EOF and refills on a loop', async () => {
    const fixture = await readFile(new URL('fixtures/tail.mp4', import.meta.url));
    const moov = fixture.indexOf(Buffer.from('moov')) - 4;
    const padding = Buffer.alloc(8 * 1024 * 1024);
    const bytes = Buffer.concat([fixture.subarray(0, moov), padding, fixture.subarray(moov)]);
    const mdat = fixture.indexOf(Buffer.from('mdat')) - 4;
    bytes.writeUInt32BE(fixture.readUInt32BE(mdat) + padding.length, mdat);
    const requests = [];
    const controller = new AbortController();
    const video = new Video();
    let loops = 0;
    const removed = [];
    serveRanges(bytes, requests);
    installMediaSource(source => queueMicrotask(() => {
        if (++loops === 2) {
            controller.abort();
        } else {
            source.buffer.buffered = { length: 1, start: () => 1, end: () => 2 };
            video.dispatchEvent(new Event('seeking'));
        }
    }), [], removed);
    await assert.rejects(streamMotionArt(video, url, 'secret', info(bytes.length), controller.signal), { name: 'AbortError' });
    assert.equal(loops, 2);
    assert.deepEqual(removed, [[0, 2]], 'clears the previous tail when looping');
    assert.ok(requests.some(([start]) => start >= padding.length), 'fetches moov directly');
    assert.ok(requests.reduce((sum, [start, end]) => sum + end - start + 1, 0) < bytes.length / 2, 'does not download the entire file');
});

test('evicts old frames instead of accumulating the whole artwork in MSE', async () => {
    const bytes = await readFile(new URL('fixtures/faststart.mp4', import.meta.url));
    const controller = new AbortController();
    const video = new Video();
    const removed = [];
    video.currentTime = 12;
    serveRanges(bytes, []);
    installMediaSource(() => queueMicrotask(() => controller.abort()), [], removed);
    const createURL = URL.createObjectURL;
    URL.createObjectURL = object => {
        object.addEventListener('sourceopen', () => queueMicrotask(() => {
            object.buffer.buffered = { length: 1, start: () => 0, end: () => 15 };
        }));
        return createURL(object);
    };
    await assert.rejects(streamMotionArt(video, url, 'secret', info(bytes.length), controller.signal), { name: 'AbortError' });
    assert.deepEqual(removed, [[0, 7]]);
});

test('falls back to header-authenticated blobs when MSE is unavailable', async () => {
    globalThis.MediaSource = undefined;
    URL.createObjectURL = () => 'blob:fallback';
    let requests = 0;
    globalThis.fetch = async (requestUrl, options) => {
        requests++;
        assert.equal(requestUrl, url);
        assert.deepEqual(options.headers, { Authorization: 'MediaBrowser Token="secret"' });
        return new Response('video');
    };
    const video = new Video();
    await loadMotionArt(video, url, 'secret', info(5), new AbortController().signal);
    assert.equal(video.src, 'blob:fallback');
    assert.equal(requests, 1);
});

test('cancels an ignored range response before falling back', async () => {
    installMediaSource(() => {});
    let cancelled = false;
    let requests = 0;
    globalThis.fetch = async (requestUrl, options) => {
        requests++;
        if (options.headers.Range) {
            return new Response(new ReadableStream({ cancel() { cancelled = true; } }));
        }
        return new Response('video');
    };
    const video = new Video();
    await loadMotionArt(video, url, 'secret', info(5), new AbortController().signal);
    assert.equal(cancelled, true);
    assert.equal(requests, 2);
});

test('rejects malformed ranges and changed ETags without appending mixed revisions', async () => {
    installMediaSource(() => {});
    for (const headers of [
        { 'Content-Range': 'bytes 1-4/5', ETag: '"test"' },
        { 'Content-Range': 'bytes 0-4/5', ETag: '"changed"' }
    ]) {
        globalThis.fetch = async () => new Response('video', { status: 206, headers });
        await assert.rejects(streamMotionArt(new Video(), url, 'secret', info(5), new AbortController().signal), /Invalid motion artwork range/);
    }
});

test('rejects partial response bodies and unauthorized requests', async () => {
    installMediaSource(() => {});
    globalThis.fetch = async () => new Response('v', {
        status: 206, headers: { 'Content-Range': 'bytes 0-4/5', ETag: '"test"' }
    });
    await assert.rejects(streamMotionArt(new Video(), url, 'secret', info(5), new AbortController().signal), /Incomplete/);
    globalThis.fetch = async () => new Response('', { status: 401 });
    await assert.rejects(loadMotionArt(new Video(), url, 'secret', info(5), new AbortController().signal), /401/);
});

test('uses the blob fallback for a codec that MSE cannot play', async () => {
    const bytes = await readFile(new URL('fixtures/faststart.mp4', import.meta.url));
    installMediaSource(() => assert.fail('MSE must not be opened'));
    MediaSource.isTypeSupported = () => false;
    const ranges = [];
    serveRanges(bytes, ranges);
    assert.equal(await streamMotionArt(new Video(), url, 'secret', info(bytes.length), new AbortController().signal), false);
    assert.equal(ranges.length, 1);
});

test('a synchronous append failure rejects without leaving an unhandled wait', async () => {
    const bytes = await readFile(new URL('fixtures/faststart.mp4', import.meta.url));
    installMediaSource(() => {});
    const createURL = URL.createObjectURL;
    URL.createObjectURL = object => {
        object.addEventListener('sourceopen', () => queueMicrotask(() => {
            object.buffer.appendBuffer = () => { throw new Error('Quota exceeded'); };
        }));
        return createURL(object);
    };
    serveRanges(bytes, []);
    await assert.rejects(streamMotionArt(new Video(), url, 'secret', info(bytes.length), new AbortController().signal), /Quota exceeded/);
});
