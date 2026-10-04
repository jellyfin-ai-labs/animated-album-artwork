using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Playback;

/// <summary>
/// Prepares artwork before viewers open album pages.
/// </summary>
public sealed class PrepareArtworkCacheTask : IScheduledTask
{
    private readonly ILibraryManager _library;
    private readonly MotionArtLocator _locator;
    private readonly PlaybackCopyCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrepareArtworkCacheTask"/> class.
    /// </summary>
    /// <param name="library">The library manager.</param>
    /// <param name="locator">The artwork locator.</param>
    /// <param name="cache">The playback cache.</param>
    public PrepareArtworkCacheTask(ILibraryManager library, MotionArtLocator locator, PlaybackCopyCache cache)
    {
        _library = library;
        _locator = locator;
        _cache = cache;
    }

    /// <inheritdoc />
    public string Name => "Prepare artwork cache";

    /// <inheritdoc />
    public string Key => "AnimatedAlbumArtPrepareCache";

    /// <inheritdoc />
    public string Description => "Generate smaller playback copies of album motion artwork.";

    /// <inheritdoc />
    public string Category => "Animated Album Art";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var albums = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicAlbum],
            Recursive = true,
        }).OfType<MusicAlbum>().ToList();
        for (var index = 0; index < albums.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = _locator.Find(albums[index]);
            if (source is not null)
            {
                await _cache.PrepareAsync(source, cancellationToken).ConfigureAwait(false);
            }

            progress.Report((index + 1) * 100d / albums.Count);
        }

        progress.Report(100);
    }
}
