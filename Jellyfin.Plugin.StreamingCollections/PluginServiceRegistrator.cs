using Jellyfin.Plugin.StreamingCollections.Services;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.StreamingCollections;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<TmdbWatchProviderClient>();
        serviceCollection.AddSingleton<WatchProviderCache>();
        serviceCollection.AddSingleton<StreamingCollectionSyncer>();
    }
}
