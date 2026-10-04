#!/usr/bin/env python3
"""Exercise real FFmpeg conversion and authenticated serving on the local dev server.

Run dev/run.sh first. Uses disposable albums and restores their originals/settings.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parent
BASE = os.environ.get('JELLYFIN_URL', 'http://localhost:8096')
PLUGIN = '0c8d1d43-0ad6-4d51-b5ac-b39b6f46fbcb'
TOKEN = (ROOT / 'data/admin-token').read_text().strip()
LIMITED = (ROOT / 'data/limited-token').read_text().strip()


def request(path, token=TOKEN, data=None, headers=None, method=None):
    extra = dict(headers or {})
    if token:
        extra['Authorization'] = f'MediaBrowser Token="{token}"'
    if data is not None:
        extra['Content-Type'] = 'application/json'
        data = json.dumps(data).encode()
    try:
        with urllib.request.urlopen(urllib.request.Request(BASE + path, data=data, headers=extra, method=method), timeout=30) as response:
            return response.status, response.headers, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.headers, error.read()


def get(path):
    status, _, body = request(path)
    assert status == 200, (path, status)
    return json.loads(body)


def wait_copy(album):
    for _ in range(1800):
        info = get('/AnimatedAlbumArt/Albums/' + album)
        if info['IsOptimized']:
            return info
        time.sleep(0.1)
    raise AssertionError('Playback copy was not prepared')


def probe(path):
    return json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-count_frames', '-show_streams', '-show_format', '-of', 'json', str(path)]))


config_path = f'/Plugins/{PLUGIN}/Configuration'
original_config = get(config_path)
albums = {item['Name']: item['Id'] for item in get('/Items?Recursive=true&IncludeItemTypes=MusicAlbum')['Items']}
source = ROOT / 'data/media/music/Test Artist/Motion Only Album/cover-motion.mp4'
with tempfile.TemporaryDirectory(prefix='artwork-cache-integration-') as temporary:
    work = Path(temporary)
    backup = work / 'original.mp4'
    shutil.copy2(source, backup)
    try:
        config = dict(original_config, GeneratePlaybackCopies=True, PlaybackCacheMiB=1024)
        assert request(config_path, data=config)[0] == 204
        large = ROOT / 'data/large-motion.mp4'
        if not large.exists():
            large = work / 'large.mp4'
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'lavfi', '-i', 'testsrc2=s=720x720:r=30:d=26', '-an', '-pix_fmt', 'yuv420p', '-c:v', 'libx264', '-preset', 'ultrafast', '-b:v', '50M', '-minrate', '50M', '-maxrate', '50M', '-bufsize', '50M', '-x264-params', 'nal-hrd=cbr:force-cfr=1', '-g', '30', str(large)], check=True)
        shutil.copy2(large, source)
        os.utime(source, None)
        original_hash = hashlib.sha256(source.read_bytes()).hexdigest()
        album = albums['Motion Only Album']
        endpoint = '/AnimatedAlbumArt/Albums/' + album
        initial = get(endpoint)
        assert not initial['IsOptimized']
        start = time.monotonic()
        copy = wait_copy(album)
        elapsed = time.monotonic() - start
        assert copy['OriginalSize'] == source.stat().st_size
        assert copy['Size'] < copy['OriginalSize']
        assert copy['Tag'] != initial['Tag']
        status, headers, body = request(endpoint + '/Video?tag=' + urllib.parse.quote(copy['Tag']))
        assert status == 200 and len(body) == copy['Size']
        output = work / 'playback.mp4'
        output.write_bytes(body)
        media = probe(output)
        video = media['streams'][0]
        assert video['codec_name'] == 'h264' and video['pix_fmt'] == 'yuv420p'
        assert max(video['width'], video['height']) <= 720 and len(media['streams']) == 1
        assert abs(float(media['format']['duration']) - 26) < 0.1
        subprocess.run(['ffmpeg', '-v', 'error', '-xerror', '-i', str(output), '-f', 'null', '-'], check=True)
        status, headers, part = request(endpoint + '/Video?tag=' + copy['Tag'], headers={'Range': 'bytes=0-1023'})
        assert status == 206 and part == body[:1024]
        assert headers['ETag'] == '"' + copy['Tag'] + '"'
        assert request(endpoint + '/Video?tag=' + initial['Tag'], headers={'Range': 'bytes=0-1023'})[2] == source.read_bytes()[:1024]
        assert request(endpoint + '/Video?tag=obsolete')[0] == 412
        assert request(endpoint + '/Video', token=None)[0] == 401
        assert request(endpoint + '/Video', token=LIMITED)[0] == 404
        assert hashlib.sha256(source.read_bytes()).hexdigest() == original_hash
        print(f'Large artwork: {copy["OriginalSize"]:,} -> {copy["Size"]:,} bytes; {100*(1-copy["Size"]/copy["OriginalSize"]):.2f}% smaller; prepared in {elapsed:.2f}s')
        print('Cached bytes/ranges/ETag, original revision pinning, stale tag rejection, access control and unchanged original passed')

        # Four independently initialized fragments with nonzero timestamps, like
        # the deployed HEVC artwork. All decoded frames must survive conversion.
        fragment = work / 'section.mp4'
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'lavfi', '-i', 'testsrc2=s=320x320:r=24:d=2', '-an', '-c:v', 'libx265', '-preset', 'ultrafast', '-x265-params', 'pools=1:frame-threads=1:log-level=error', '-tag:v', 'hvc1', '-pix_fmt', 'yuv420p', '-g', '24', '-video_track_timescale', '12288', '-output_ts_offset', '10', '-movflags', 'empty_moov+frag_keyframe+default_base_moof', str(fragment)], check=True)
        section = bytearray(fragment.read_bytes())

        def offset_fragments(start, end):
            position = start
            while position < end:
                size = int.from_bytes(section[position:position+4], 'big')
                kind = section[position+4:position+8]
                header_size = 8
                if size == 1:
                    size = int.from_bytes(section[position+8:position+16], 'big')
                    header_size = 16
                if size == 0:
                    size = end - position
                assert size >= header_size and position + size <= end
                payload = position + header_size
                if kind in (b'moof', b'traf'):
                    offset_fragments(payload, position + size)
                elif kind == b'tfdt':
                    width = 8 if section[payload] == 1 else 4
                    field = payload + 4
                    value = int.from_bytes(section[field:field+width], 'big')
                    # FFmpeg's MOV muxer uses a 1/12288 video timescale here.
                    value += 10 * 12288
                    section[field:field+width] = value.to_bytes(width, 'big')
                position += size

        offset_fragments(0, len(section))
        fragment.write_bytes(section)
        assert float(probe(fragment)['format']['start_time']) >= 10
        source.write_bytes(section * 4)
        assert request(endpoint + '/Video?tag=' + copy['Tag'])[0] == 412
        repeated = wait_copy(album)
        output.write_bytes(request(endpoint + '/Video?tag=' + repeated['Tag'])[2])
        repeated_media = probe(output)
        assert abs(float(repeated_media['format']['duration']) - 8) < 0.1, repeated_media['format']
        assert int(repeated_media['streams'][0]['nb_read_frames']) == 192
        print('Repeated HEVC fragments starting at 10 seconds: all 192 frames retained, duration 8 seconds, old copy invalidated')

        # Downscaling, frame-rate limiting, and removal of audio use the actual encoder.
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'lavfi', '-i', 'testsrc2=s=1280x960:r=60:d=2', '-f', 'lavfi', '-i', 'sine=frequency=440:duration=2', '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p', '-c:a', 'aac', str(source)], check=True)
        limited_copy = wait_copy(album)
        output.write_bytes(request(endpoint + '/Video?tag=' + limited_copy['Tag'])[2])
        limited_media = probe(output)
        limited_video = limited_media['streams'][0]
        assert len(limited_media['streams']) == 1
        assert (limited_video['width'], limited_video['height']) == (720, 540)
        assert limited_video['avg_frame_rate'] == '30/1' and int(limited_video['nb_read_frames']) == 60
        print('1280×960/60 fps with audio -> 720×540/30 fps silent copy passed')
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'lavfi', '-i', 'testsrc2=s=360x720:r=24:d=2', '-vf', 'setsar=2', '-an', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', str(source)], check=True)
        square_copy = wait_copy(album)
        output.write_bytes(request(endpoint + '/Video?tag=' + square_copy['Tag'])[2])
        square_video = probe(output)['streams'][0]
        assert (square_video['width'], square_video['height']) == (360, 360)
        assert square_video['sample_aspect_ratio'] == '1:1'
        print('Anamorphic source display aspect ratio preserved in square-pixel copy')
        repeated = square_copy

        config['GeneratePlaybackCopies'] = False
        assert request(config_path, data=config)[0] == 204
        disabled = get(endpoint)
        assert not disabled['IsOptimized'] and disabled['Size'] == source.stat().st_size
        assert request(endpoint + '/Video?tag=' + repeated['Tag'])[0] == 412
        config['GeneratePlaybackCopies'] = True
        assert request(config_path, data=config)[0] == 204
        tasks = get('/ScheduledTasks')
        task = next(item for item in tasks if item['Key'] == 'AnimatedAlbumArtPrepareCache')
        assert request('/ScheduledTasks/Running/' + task['Id'], method='POST')[0] == 204
        for _ in range(600):
            state = get('/ScheduledTasks/' + task['Id'])
            if state['State'] == 'Idle' and state.get('LastExecutionResult'):
                assert state['LastExecutionResult']['Status'] == 'Completed', state
                break
            time.sleep(0.1)
        else:
            raise AssertionError('Scheduled cache preparation did not complete')
        print('Optimization toggle and registered scheduled task passed')
    finally:
        shutil.copy2(backup, source)
        request(config_path, data=original_config)
