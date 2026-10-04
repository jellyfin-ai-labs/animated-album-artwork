using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Playback;

/// <summary>
/// Generates and validates silent, browser-compatible playback copies.
/// </summary>
public sealed class PlaybackCopyEncoder
{
    private readonly IMediaEncoder _encoder;
    private readonly MotionArtProbe _probe;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackCopyEncoder"/> class.
    /// </summary>
    /// <param name="encoder">Jellyfin's configured encoder.</param>
    /// <param name="probe">The artwork probe.</param>
    public PlaybackCopyEncoder(IMediaEncoder encoder, MotionArtProbe probe)
    {
        _encoder = encoder;
        _probe = probe;
    }

    /// <summary>
    /// Encodes and validates a temporary file; the cache publishes it afterward.
    /// </summary>
    /// <param name="source">The original artwork.</param>
    /// <param name="destination">The temporary MP4 path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing completion.</returns>
    public async Task EncodeAsync(MotionArtFile source, string destination, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var prepared = await ArtworkEncoderInput.PrepareAsync(source, destination, timeout.Token).ConfigureAwait(false);
        var input = await _probe.ProbeAsync(prepared.Paths[0], timeout.Token, countFrames: true).ConfigureAwait(false);
        var video = input.Streams.FirstOrDefault(stream => stream.CodecType == "video")
            ?? throw new InvalidOperationException("Artwork has no video stream.");
        var rate = ParseRate(video.AverageFrameRate);
        if (rate <= 0 || !double.IsFinite(rate))
        {
            throw new InvalidOperationException("Artwork has no usable frame rate.");
        }

        long frames = 0;
        foreach (var path in prepared.Paths)
        {
            var section = path == prepared.Paths[0] ? input : await _probe.ProbeAsync(path, timeout.Token, countFrames: true).ConfigureAwait(false);
            var stream = section.Streams.FirstOrDefault(item => item.CodecType == "video");
            if (stream is null || stream.CodecName != video.CodecName || stream.Width != video.Width || stream.Height != video.Height
                || Math.Abs(ParseRate(stream.AverageFrameRate) - rate) > 0.01
                || !long.TryParse(stream.ReadFrames, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
            {
                throw new InvalidOperationException("Artwork sections do not have consistent, decodable video streams.");
            }

            frames = checked(frames + count);
        }

        // Rebuild timestamps from decoded frames. Repeated initialization sections
        // and nonzero/discontinuous source timestamps must not drop whole sections.
        var filter = string.Create(CultureInfo.InvariantCulture, $"setpts=N/({rate:R}*TB),scale=w='min(720,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2:reset_sar=1,setsar=1,fps={Math.Min(30, rate):R}");
        var arguments = new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y" }
            .Concat(prepared.Arguments).Concat(new[]
            {
                "-map", "0:v:0", "-an", "-sn", "-dn", "-map_metadata", "-1",
                "-vf", filter, "-c:v", "libx264", "-preset", "medium", "-crf", "23",
                "-maxrate", "4M", "-bufsize", "8M", "-threads", "2", "-pix_fmt", "yuv420p",
                "-g", Math.Max(1, (int)Math.Round(Math.Min(30, rate) * 2)).ToString(CultureInfo.InvariantCulture),
                "-movflags", "+faststart", "-f", "mp4", destination,
            }).ToArray();
        await RunAsync(_encoder.EncoderPath, arguments, timeout.Token).ConfigureAwait(false);

        var output = await _probe.ProbeAsync(destination, timeout.Token, countFrames: true).ConfigureAwait(false);
        var encoded = output.Streams.SingleOrDefault(stream => stream.CodecType == "video");
        if (encoded?.CodecName != "h264" || encoded.PixelFormat != "yuv420p"
            || encoded.Width is not > 0 or > 720 || encoded.Height is not > 0 or > 720
            || output.Streams.Any(stream => stream.CodecType == "audio")
            || !double.TryParse(output.Format?.Duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            || Math.Abs(duration - (frames / rate)) > 0.1
            || !long.TryParse(encoded.ReadFrames, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outputFrames)
            || Math.Abs(outputFrames - (frames * Math.Min(30, rate) / rate)) > 2)
        {
            throw new InvalidOperationException("Playback copy failed format validation.");
        }

        // Probe success alone cannot detect damaged frames. Decode the complete
        // generated copy before making it available to any viewer.
        await RunAsync(
            _encoder.EncoderPath,
            new[]
        {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-xerror", "-i", destination,
            "-map", "0:v:0", "-f", "null", "-",
        },
            timeout.Token).ConfigureAwait(false);
    }

    private static double ParseRate(string? value)
    {
        var parts = value?.Split('/');
        return parts?.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && denominator > 0 ? numerator / denominator : 0;
    }

    private static async Task RunAsync(string executable, string[] arguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(executable))
        {
            throw new InvalidOperationException("FFmpeg is not configured.");
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("FFmpeg could not start.");
        try
        {
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("FFmpeg failed: " + (await error.ConfigureAwait(false)).Trim());
            }

            await error.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }
}
