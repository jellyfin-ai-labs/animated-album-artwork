using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether motion artwork is discovered and served.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the sidecar extensions to serve, comma separated, in priority order.
    /// Only extensions in <see cref="MotionArt.MotionArtFormats.Supported"/> are honored.
    /// </summary>
    public string AllowedExtensions { get; set; } = ".mp4,.mov";

    /// <summary>
    /// Gets or sets a value indicating whether the bundled client script is injected into Jellyfin Web.
    /// </summary>
    public bool InjectWebClient { get; set; } = true;
}
