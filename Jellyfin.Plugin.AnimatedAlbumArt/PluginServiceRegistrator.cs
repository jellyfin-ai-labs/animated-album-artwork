using Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using Jellyfin.Plugin.AnimatedAlbumArt.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AnimatedAlbumArt;

/// <summary>
/// Registers the plugin's services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<MotionArtLocator>();
        serviceCollection.AddSingleton<MotionArtProbe>();
        serviceCollection.AddTransient<IStartupFilter, WebClientInjectionStartupFilter>();
    }
}
