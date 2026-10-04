using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;

/// <summary>
/// Compares a probed file with Apple Music's Album Motion delivery requirements.
/// The result is advisory: files that fail any check are still served.
/// </summary>
/// <remarks>See https://help.apple.com/itc/albummotionguide/en.lproj/static.html.</remarks>
public static class AppleMotionProfile
{
    private static readonly (int Width, int Height, string Name)[] _sizes =
    [
        (3840, 3840, "1x1"),
        (2048, 2732, "3x4"),
    ];

    private static readonly double[] _frameRates = [24000.0 / 1001, 24, 25, 30000.0 / 1001, 30];

    /// <summary>
    /// Evaluates a probe result.
    /// </summary>
    /// <param name="probe">The ffprobe output.</param>
    /// <returns>One check per requirement.</returns>
    public static IReadOnlyList<ProfileCheck> Evaluate(ProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var video = probe.Streams.FirstOrDefault(s => s.CodecType == "video");
        var hasAudio = probe.Streams.Any(s => s.CodecType == "audio");
        var formatName = probe.Format?.FormatName;
        var duration = ParseDouble(probe.Format?.Duration);
        var bitRate = ParseDouble(probe.Format?.BitRate);
        var frameRate = ParseFraction(video?.AverageFrameRate);
        var size = video?.Width is int w && video.Height is int h ? $"{w}x{h}" : null;

        return
        [
            new("Container", formatName is null ? null : formatName.Contains("mp4", StringComparison.Ordinal) || formatName.Contains("mov", StringComparison.Ordinal), "MP4 or MOV", formatName),
            new("Video codec", video?.CodecName is null ? null : video.CodecName is "h264" or "prores", "H.264 or Apple ProRes", video?.CodecName),
            new("Dimensions", size is null ? null : _sizes.Any(s => s.Width == video!.Width && s.Height == video.Height), "3840x3840 (1x1) or 2048x2732 (3x4)", size),
            new("Duration", duration is null ? null : duration is >= 8 and <= 35, "8-35 seconds", duration?.ToString("0.###", CultureInfo.InvariantCulture)),
            new("Frame rate", frameRate is null ? null : _frameRates.Any(r => Math.Abs(r - frameRate.Value) < 0.01), "23.976, 24, 25, 29.97 or 30 fps", frameRate?.ToString("0.###", CultureInfo.InvariantCulture)),
            new("Audio", !hasAudio, "No audio track", hasAudio ? "Audio track present" : "None"),
            new("Bit rate", bitRate is null ? null : bitRate is >= 45_000_000 and <= 100_000_000, "45-100 Mbps", bitRate is null ? null : (bitRate.Value / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + " Mbps"),
            new("Pixel aspect ratio", video?.SampleAspectRatio is null or "0:1" ? null : video.SampleAspectRatio == "1:1", "1:1 (square pixels)", video?.SampleAspectRatio),
            new("Color primaries", video?.ColorPrimaries is null or "unknown" ? null : video.ColorPrimaries is "bt709", "Rec. 709 or sRGB", video?.ColorPrimaries),
        ];
    }

    private static double? ParseDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static double? ParseFraction(string? value)
    {
        var parts = value?.Split('/');
        if (parts is not [var numerator, var denominator])
        {
            return ParseDouble(value);
        }

        var n = ParseDouble(numerator);
        var d = ParseDouble(denominator);
        return n is null || d is null or 0 ? null : n / d;
    }
}
