using Jellyfin.Plugin.RequestMovies.Library;
using Jellyfin.Plugin.RequestMovies.Seerr;
using Jellyfin.Plugin.RequestMovies.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.RequestMovies;

/// <summary>Enregistre les services du plugin dans l'injection de dépendances.</summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("RequestMovies.Seerr");
        serviceCollection.AddHttpClient("RequestMovies.Tmdb");
        serviceCollection.AddSingleton<SeerrClient>();
        serviceCollection.AddSingleton<JellyfinLibraryService>();
        serviceCollection.AddSingleton<UserMapper>();
        serviceCollection.AddSingleton<MediaService>();
        serviceCollection.AddSingleton<AvailabilityService>();
        serviceCollection.AddSingleton<RateLimiter>();
        serviceCollection.AddSingleton<TextRequestStore>();
        serviceCollection.AddSingleton<TextRequestAvailabilityService>();
    }
}
