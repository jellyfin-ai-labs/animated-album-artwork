using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;

/// <summary>
/// The sidecar naming convention and the video formats it may use.
/// </summary>
public static class MotionArtFormats
{
    /// <summary>
    /// The sidecar file name, without extension, looked for in an album folder.
    /// </summary>
    public const string BaseName = "cover-motion";

    /// <summary>
    /// Gets the extensions that may be served, mapped to their content types.
    /// </summary>
    public static FrozenDictionary<string, string> Supported { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".webm"] = "video/webm",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a comma separated extension list, keeping order and dropping unsupported or duplicate entries.
    /// </summary>
    /// <param name="value">The configured list, e.g. ".mp4, mov".</param>
    /// <returns>Normalized lower-case extensions with a leading dot.</returns>
    public static IReadOnlyList<string> ParseExtensions(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ext => (ext.StartsWith('.') ? ext : "." + ext).ToLowerInvariant())
            .Where(Supported.ContainsKey)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Determines whether a file name follows the sidecar convention for any supported format.
    /// </summary>
    /// <param name="fileName">A file name without directory.</param>
    /// <returns><c>true</c> for names such as <c>cover-motion.mp4</c>.</returns>
    public static bool IsSidecarName(string fileName)
    {
        return string.Equals(System.IO.Path.GetFileNameWithoutExtension(fileName), BaseName, StringComparison.OrdinalIgnoreCase)
            && Supported.ContainsKey(System.IO.Path.GetExtension(fileName));
    }
}
