using System;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Api;

/// <summary>
/// Describes an album's motion artwork.
/// </summary>
public sealed class MotionArtInfo
{
    /// <summary>
    /// Gets or sets the album id.
    /// </summary>
    public Guid AlbumId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the album has motion artwork.
    /// </summary>
    public bool HasMotionArt { get; set; }

    /// <summary>
    /// Gets or sets the video content type.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// Gets or sets a tag that changes when the file changes; clients may use it to bust caches.
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether playback uses a generated copy.
    /// </summary>
    public bool IsOptimized { get; set; }

    /// <summary>
    /// Gets or sets the original artwork size in bytes.
    /// </summary>
    public long? OriginalSize { get; set; }
}
