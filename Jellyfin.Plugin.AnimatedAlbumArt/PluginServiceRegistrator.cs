using Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using Jellyfin.Plugin.AnimatedAlbumArt.Playback;
using Jellyfin.Plugin.AnimatedAlbumArt.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        serviceCollection.AddSingleton<PlaybackCopyEncoder>();
        serviceCollection.AddSingleton<PlaybackCopyCache>();
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<PlaybackCopyCache>());
        serviceCollection.AddSingleton<PrepareArtworkCacheTask>();
        serviceCollection.AddTransient<IStartupFilter, WebClientInjectionStartupFilter>();
    }
}
