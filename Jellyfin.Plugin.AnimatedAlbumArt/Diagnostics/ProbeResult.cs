using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;

/// <summary>
/// The subset of <c>ffprobe -print_format json -show_format -show_streams</c> output used for diagnostics.
/// </summary>
public sealed class ProbeResult
{
    /// <summary>
    /// Gets or sets the streams.
    /// </summary>
    [JsonPropertyName("streams")]
    public IReadOnlyList<ProbeStreamInfo> Streams { get; set; } = [];

    /// <summary>
    /// Gets or sets the container information.
    /// </summary>
    [JsonPropertyName("format")]
    public ProbeFormat? Format { get; set; }
}
