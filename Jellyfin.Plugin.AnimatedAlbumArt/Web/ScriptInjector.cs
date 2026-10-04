using System;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Web;

/// <summary>
/// Adds the client script tag to the Jellyfin Web index page.
/// </summary>
public static class ScriptInjector
{
    /// <summary>
    /// The script tag. The path is relative to <c>/web/</c> so it works under any base URL.
    /// </summary>
    public const string ScriptTag = "<script defer src=\"../AnimatedAlbumArt/ClientScript\" data-animated-album-art></script>";

    /// <summary>
    /// Inserts <see cref="ScriptTag"/> before the closing body tag.
    /// </summary>
    /// <param name="html">The index page.</param>
    /// <returns>The page with the script tag, or the page unchanged if it already has it or has no body.</returns>
    public static string Inject(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (html.Contains("data-animated-album-art", StringComparison.Ordinal))
        {
            return html;
        }

        var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? html : html.Insert(index, ScriptTag);
    }

    /// <summary>
    /// Determines whether a request path is the Jellyfin Web index page.
    /// </summary>
    /// <param name="path">The full request path, including any base URL.</param>
    /// <returns><c>true</c> for <c>/web/</c> and <c>/web/index.html</c>.</returns>
    public static bool IsIndexPath(string? path)
    {
        return path is not null
            && (path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase));
    }
}
