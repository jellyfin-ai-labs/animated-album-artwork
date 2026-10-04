using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Web;

/// <summary>
/// Rewrites the Jellyfin Web index page to load the client script, so no files on disk are modified.
/// </summary>
public class WebClientInjectionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebClientInjectionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    public WebClientInjectionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Handles a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (Plugin.Instance?.Configuration.InjectWebClient != true
            || !HttpMethods.IsGet(request.Method)
            || !ScriptInjector.IsIndexPath(request.PathBase + request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Ask for an uncompressed, complete page so it can be edited.
        request.Headers.Remove(HeaderNames.AcceptEncoding);
        request.Headers.Remove(HeaderNames.IfNoneMatch);
        request.Headers.Remove(HeaderNames.IfModifiedSince);

        var response = context.Response;
        var originalBody = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            response.Body = originalBody;
        }

        buffer.Position = 0;
        if (response.StatusCode != StatusCodes.Status200OK
            || response.ContentType?.StartsWith("text/html", System.StringComparison.OrdinalIgnoreCase) != true
            || response.Headers.ContentEncoding.Count > 0)
        {
            await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        string html;
        using (var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: true))
        {
            html = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        }

        var bytes = Encoding.UTF8.GetBytes(ScriptInjector.Inject(html));
        response.ContentLength = bytes.Length;
        response.Headers.Remove(HeaderNames.ETag);
        response.Headers.Remove(HeaderNames.LastModified);
        response.Headers.CacheControl = "no-cache";
        await originalBody.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }
}
