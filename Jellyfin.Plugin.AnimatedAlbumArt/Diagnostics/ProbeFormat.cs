using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;

/// <summary>
/// The ffprobe container information.
/// </summary>
public sealed class ProbeFormat
{
    /// <summary>
    /// Gets or sets the demuxer names, e.g. <c>mov,mp4,m4a,3gp,3g2,mj2</c>.
    /// </summary>
    [JsonPropertyName("format_name")]
    public string? FormatName { get; set; }

    /// <summary>
    /// Gets or sets the duration in seconds.
    /// </summary>
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    /// <summary>
    /// Gets or sets the overall bit rate in bits per second.
    /// </summary>
    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; set; }
}
