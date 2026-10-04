using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities.Audio;

namespace Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;

/// <summary>
/// Finds the motion artwork sidecar for an album.
/// The file system is checked on every lookup, so added, replaced or deleted sidecars
/// take effect immediately without a library scan and nothing is persisted.
/// </summary>
public class MotionArtLocator
{
    private static readonly EnumerationOptions _enumerationOptions = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
    };

    /// <summary>
    /// Finds the motion artwork for an album using the current plugin configuration.
    /// </summary>
    /// <param name="album">The album.</param>
    /// <returns>The sidecar, or <c>null</c> when there is none or the plugin is disabled.</returns>
    public virtual MotionArtFile? Find(MusicAlbum album)
    {
        ArgumentNullException.ThrowIfNull(album);

        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled || !album.IsFileProtocol || string.IsNullOrEmpty(album.Path))
        {
            return null;
        }

        return FindInDirectory(album.Path, MotionArtFormats.ParseExtensions(config.AllowedExtensions));
    }

    /// <summary>
    /// Finds the sidecar in a directory. Names match case-insensitively; when several
    /// extensions are present the earliest in <paramref name="extensionsByPriority"/> wins.
    /// </summary>
    /// <param name="directory">The album directory.</param>
    /// <param name="extensionsByPriority">Allowed extensions, most preferred first.</param>
    /// <returns>The sidecar, or <c>null</c> if none is present.</returns>
    public static MotionArtFile? FindInDirectory(string directory, IReadOnlyList<string> extensionsByPriority)
    {
        ArgumentNullException.ThrowIfNull(extensionsByPriority);

        if (extensionsByPriority.Count == 0 || !Directory.Exists(directory))
        {
            return null;
        }

        var candidates = Directory
            .EnumerateFiles(directory, MotionArtFormats.BaseName + ".*", _enumerationOptions)
            .Where(path => MotionArtFormats.IsSidecarName(Path.GetFileName(path)))
            .ToArray();

        foreach (var extension in extensionsByPriority)
        {
            var match = candidates
                .Where(path => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
            if (match is not null)
            {
                var info = new FileInfo(match);
                return new MotionArtFile(info.FullName, MotionArtFormats.Supported[extension], info.Length, info.LastWriteTimeUtc);
            }
        }

        return null;
    }
}
