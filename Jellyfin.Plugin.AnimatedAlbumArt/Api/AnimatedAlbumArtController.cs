using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Api;

/// <summary>
/// Serves motion artwork. Albums are addressed by id only and are looked up as the
/// calling user, so library access and parental controls apply.
/// </summary>
[ApiController]
[Authorize]
[Route("AnimatedAlbumArt")]
public class AnimatedAlbumArtController : ControllerBase
{
    private const string RequiresElevationPolicy = "RequiresElevation";

    private readonly ILibraryManager _libraryManager;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly MotionArtLocator _locator;
    private readonly MotionArtProbe _probe;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimatedAlbumArtController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="authorizationContext">Instance of the <see cref="IAuthorizationContext"/> interface.</param>
    /// <param name="locator">The motion artwork locator.</param>
    /// <param name="probe">The motion artwork probe.</param>
    public AnimatedAlbumArtController(
        ILibraryManager libraryManager,
        IAuthorizationContext authorizationContext,
        MotionArtLocator locator,
        MotionArtProbe probe)
    {
        _libraryManager = libraryManager;
        _authorizationContext = authorizationContext;
        _locator = locator;
        _probe = probe;
    }

    /// <summary>
    /// Gets whether an album has motion artwork.
    /// </summary>
    /// <param name="albumId">The album id.</param>
    /// <response code="200">The album exists and is visible to the user.</response>
    /// <response code="404">The album does not exist or the user cannot access it.</response>
    /// <returns>The motion artwork description.</returns>
    [HttpGet("Albums/{albumId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MotionArtInfo>> GetInfo([FromRoute] Guid albumId)
    {
        var album = await GetVisibleAlbum(albumId).ConfigureAwait(false);
        if (album is null)
        {
            return NotFound();
        }

        var file = _locator.Find(album);
        return new MotionArtInfo
        {
            AlbumId = album.Id,
            HasMotionArt = file is not null,
            ContentType = file?.ContentType,
            Size = file?.Length,
            Tag = file?.Tag,
        };
    }

    /// <summary>
    /// Streams an album's motion artwork. Supports byte ranges and conditional requests.
    /// </summary>
    /// <param name="albumId">The album id.</param>
    /// <response code="200">The whole video file.</response>
    /// <response code="206">Part of the video file.</response>
    /// <response code="404">No motion artwork, or the user cannot access the album.</response>
    /// <returns>The video file.</returns>
    [HttpGet("Albums/{albumId}/Video")]
    [HttpHead("Albums/{albumId}/Video")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetVideo([FromRoute] Guid albumId)
    {
        var album = await GetVisibleAlbum(albumId).ConfigureAwait(false);
        var file = album is null ? null : _locator.Find(album);
        if (file is null)
        {
            return NotFound();
        }

        // Revalidate on every use so a replaced sidecar is picked up; unchanged files get a 304.
        Response.Headers.CacheControl = "private, no-cache";
        return PhysicalFile(
            file.Path,
            file.ContentType,
            file.LastModified,
            new EntityTagHeaderValue($"\"{file.Tag}\""),
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Probes an album's motion artwork and reports advisory checks against Apple's Album Motion requirements.
    /// </summary>
    /// <param name="albumId">The album id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The checks were run.</response>
    /// <response code="404">No motion artwork, or the album does not exist.</response>
    /// <returns>The diagnostics.</returns>
    [HttpGet("Albums/{albumId}/Diagnostics")]
    [Authorize(Policy = RequiresElevationPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MotionArtDiagnostics>> GetDiagnostics([FromRoute] Guid albumId, CancellationToken cancellationToken)
    {
        var album = await GetVisibleAlbum(albumId).ConfigureAwait(false);
        var file = album is null ? null : _locator.Find(album);
        if (file is null)
        {
            return NotFound();
        }

        var probe = await _probe.ProbeAsync(file.Path, cancellationToken).ConfigureAwait(false);
        return new MotionArtDiagnostics
        {
            AlbumId = albumId,
            Path = file.Path,
            AppleMotionChecks = AppleMotionProfile.Evaluate(probe),
        };
    }

    /// <summary>
    /// Gets the Jellyfin Web client script. Anonymous because it is loaded by a script tag.
    /// </summary>
    /// <response code="200">The script is returned.</response>
    /// <returns>The script.</returns>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".Web.animatedAlbumArt.js");
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        return File(stream, "application/javascript; charset=utf-8");
    }

    private async Task<MusicAlbum?> GetVisibleAlbum(Guid albumId)
    {
        var user = (await _authorizationContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false)).User;
        if (user is null)
        {
            return null;
        }

        var album = _libraryManager.GetItemById<MusicAlbum>(albumId, user);
        return album is not null && album.IsVisible(user) ? album : null;
    }
}
