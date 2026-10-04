using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Web;

/// <summary>
/// Adds <see cref="WebClientInjectionMiddleware"/> to the front of the server's request pipeline.
/// </summary>
public class WebClientInjectionStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<WebClientInjectionMiddleware>();
            next(app);
        };
    }
}
