using System;
using System.Globalization;

namespace Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;

/// <summary>
/// A motion artwork sidecar found on disk.
/// </summary>
/// <param name="Path">The full file path.</param>
/// <param name="ContentType">The HTTP content type.</param>
/// <param name="Length">The file size in bytes.</param>
/// <param name="LastModified">The last write time.</param>
public sealed record MotionArtFile(string Path, string ContentType, long Length, DateTimeOffset LastModified)
{
    /// <summary>
    /// Gets an opaque tag that changes when the file is replaced or edited.
    /// </summary>
    public string Tag => string.Create(CultureInfo.InvariantCulture, $"{Length:x}-{LastModified.UtcTicks:x}");
}
