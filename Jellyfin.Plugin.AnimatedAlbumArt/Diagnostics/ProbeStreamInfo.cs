using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;

/// <summary>
/// An ffprobe stream.
/// </summary>
public sealed class ProbeStreamInfo
{
    /// <summary>
    /// Gets or sets the codec type, e.g. <c>video</c> or <c>audio</c>.
    /// </summary>
    [JsonPropertyName("codec_type")]
    public string? CodecType { get; set; }

    /// <summary>
    /// Gets or sets the codec name, e.g. <c>h264</c> or <c>prores</c>.
    /// </summary>
    [JsonPropertyName("codec_name")]
    public string? CodecName { get; set; }

    /// <summary>
    /// Gets or sets the width in pixels.
    /// </summary>
    [JsonPropertyName("width")]
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets the height in pixels.
    /// </summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    /// <summary>
    /// Gets or sets the average frame rate as a fraction, e.g. <c>30000/1001</c>.
    /// </summary>
    [JsonPropertyName("avg_frame_rate")]
    public string? AverageFrameRate { get; set; }

    /// <summary>
    /// Gets or sets the sample aspect ratio, e.g. <c>1:1</c>.
    /// </summary>
    [JsonPropertyName("sample_aspect_ratio")]
    public string? SampleAspectRatio { get; set; }

    /// <summary>
    /// Gets or sets the color primaries, e.g. <c>bt709</c>.
    /// </summary>
    [JsonPropertyName("color_primaries")]
    public string? ColorPrimaries { get; set; }

    /// <summary>
    /// Gets or sets the pixel format, e.g. <c>yuv420p</c>.
    /// </summary>
    [JsonPropertyName("pix_fmt")]
    public string? PixelFormat { get; set; }
}
