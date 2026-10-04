import { loadMotionArt } from './motionArtStreaming.js';

/*
 * Animated Album Art - Jellyfin Web client.
 *
 * Plays an album's motion artwork over the static cover on the album detail page.
 * Motion artwork wins when an album has both; the static cover stays visible while
 * the video loads, if it fails to load or play, and when the viewer prefers reduced
 * motion.
 */
(function () {
    'use strict';

    if (window.animatedAlbumArtLoaded) {
        return;
    }
    window.animatedAlbumArtLoaded = true;

    var VIDEO_CLASS = 'animatedAlbumArtVideo';
    var reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    var infoRequests = new Map();
    var failures = new Set();
    var scanQueued = false;

    var style = document.createElement('style');
    style.textContent =
        '.' + VIDEO_CLASS + '{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;' +
        'border-radius:inherit;pointer-events:none;opacity:0;transition:opacity .4s ease}' +
        '.' + VIDEO_CLASS + '.playing{opacity:1}';
    document.head.appendChild(style);

    function detailItemId() {
        var hash = window.location.hash;
        var query = hash.indexOf('?');
        if (query < 0 || !/^#!?\/details\b/i.test(hash)) {
            return null;
        }
        return new URLSearchParams(hash.slice(query + 1)).get('id');
    }

    function getInfo(apiClient, id) {
        if (!infoRequests.has(id)) {
            // 404 for anything that is not an album the user can see.
            var request = apiClient.getJSON(apiClient.getUrl('AnimatedAlbumArt/Albums/' + id))
                .catch(function () { return null; });
            infoRequests.set(id, request);
        }
        return infoRequests.get(id);
    }

    // Jellyfin keeps recent pages in the DOM, hidden, and renders a second cover for
    // the mobile layout; only covers that are actually displayed get a video.
    function isShown(element) {
        return element.getClientRects().length > 0;
    }

    function removeVideos(keepId) {
        document.querySelectorAll('video.' + VIDEO_CLASS).forEach(function (video) {
            if (video.dataset.itemId !== keepId || !isShown(video)) {
                disposeVideo(video);
            }
        });
    }

    function disposeVideo(video) {
        video.animatedAlbumArtAbort.abort();
        if (video.src) {
            URL.revokeObjectURL(video.src);
            video.removeAttribute('src');
            video.load();
        }
        video.remove();
    }

    function attach(apiClient, container, id, info) {
        var key = id + ':' + info.Tag;
        if (container.querySelector('video.' + VIDEO_CLASS) || failures.has(key)) {
            return;
        }

        // Any failure (network, unsupported codec) leaves the static cover in place.
        // Remember it, or removing the video would trigger a rescan that retries forever.
        function fail() {
            failures.add(key);
            disposeVideo(video);
        }

        var video = document.createElement('video');
        video.className = VIDEO_CLASS;
        video.dataset.itemId = id;
        video.muted = true;
        video.defaultMuted = true;
        video.loop = true;
        video.autoplay = true;
        video.playsInline = true;
        video.disablePictureInPicture = true;
        video.setAttribute('aria-hidden', 'true');
        video.addEventListener('playing', function () {
            video.classList.add('playing');
        }, { once: true });
        video.addEventListener('error', fail, { once: true });
        video.animatedAlbumArtAbort = new AbortController();

        if (window.getComputedStyle(container).position === 'static') {
            container.style.position = 'relative';
        }
        container.appendChild(video);

        var url = apiClient.getUrl('AnimatedAlbumArt/Albums/' + id + '/Video', { tag: info.Tag });
        loadMotionArt(video, url, apiClient.accessToken(), info, video.animatedAlbumArtAbort.signal).catch(function () {
            // A video removed mid-download was aborted, not failed.
            if (video.isConnected) {
                fail();
            }
        });
    }

    function scan() {
        scanQueued = false;

        var id = detailItemId();
        var apiClient = window.ApiClient;
        removeVideos(reducedMotion.matches ? null : id);
        if (!id || !apiClient || reducedMotion.matches) {
            return;
        }

        var containers = Array.prototype.filter.call(document.querySelectorAll(
            '.itemDetailPage:not(.hide) .detailImageContainer .cardImageContainer'), isShown);
        if (!containers.length) {
            return;
        }

        getInfo(apiClient, id).then(function (info) {
            if (!info || !info.HasMotionArt || detailItemId() !== id || reducedMotion.matches) {
                return;
            }
            containers.forEach(function (container) {
                if (isShown(container)) {
                    attach(apiClient, container, id, info);
                }
            });
        });
    }

    function queueScan() {
        if (!scanQueued) {
            scanQueued = true;
            window.requestAnimationFrame(scan);
        }
    }

    // Jellyfin Web is a single-page app that re-renders detail pages in place and
    // hides pages by toggling classes, so watch both.
    new MutationObserver(queueScan).observe(document.body, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ['class']
    });
    // The mobile and desktop covers swap on resize.
    window.addEventListener('resize', queueScan);
    window.addEventListener('hashchange', function () {
        infoRequests.clear();
        failures.clear();
        queueScan();
    });
    reducedMotion.addEventListener('change', queueScan);
    queueScan();
})();
