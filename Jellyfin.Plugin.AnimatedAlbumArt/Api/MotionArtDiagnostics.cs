using System;
using System.Collections.Generic;
using Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Api;

/// <summary>
/// Diagnostics for an album's motion artwork.
/// </summary>
public sealed class MotionArtDiagnostics
{
    /// <summary>
    /// Gets or sets the album id.
    /// </summary>
    public Guid AlbumId { get; set; }

    /// <summary>
    /// Gets or sets the sidecar file path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the advisory Apple Album Motion checks.
    /// </summary>
    public IReadOnlyList<ProfileCheck> AppleMotionChecks { get; set; } = [];
}
