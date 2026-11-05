using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RequestMovies.Library;

/// <summary>Accès à la bibliothèque Jellyfin : présence réelle d'un média, qualité, épisodes, rafraîchissement.</summary>
public sealed class JellyfinLibraryService
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly ILogger<JellyfinLibraryService> _logger;
    private int _refreshRunning;

    public JellyfinLibraryService(ILibraryManager library, IUserManager users, ILogger<JellyfinLibraryService> logger)
    {
        _library = library;
        _users = users;
        _logger = logger;
    }

    public bool RefreshRunning => Volatile.Read(ref _refreshRunning) == 1;

    /// <summary>Films ou séries de la bibliothèque portant cet ID TMDB (visibles par l'utilisateur, ou tous si null).</summary>
    public IReadOnlyList<BaseItem> FindByTmdb(Guid? userId, string mediaType, int tmdbId)
    {
        var user = userId is { } id ? _users.GetUserById(id) : null;
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { mediaType == "tv" ? BaseItemKind.Series : BaseItemKind.Movie },
            HasAnyProviderId = new Dictionary<string, string>
            {
                ["Tmdb"] = tmdbId.ToString(CultureInfo.InvariantCulture)
            },
            IsVirtualItem = false,
            Recursive = true,
            Limit = 5
        };
        return _library.GetItemList(query);
    }

    /// <summary>Finds movies or series in Jellyfin with the specified IMDb title ID.</summary>
    public IReadOnlyList<BaseItem> FindByImdb(string imdbId)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            HasAnyProviderId = new Dictionary<string, string>
            {
                ["Imdb"] = imdbId
            },
            IsVirtualItem = false,
            Recursive = true,
            Limit = 5
        };
        return _library.GetItemList(query);
    }

    /// <summary>Vrai si l'un des items est en 4K (largeur ou hauteur suffisante).</summary>
    public static bool Has4K(IEnumerable<BaseItem> items)
        => items.Any(i => i.Height >= 2000 || i.Width >= 3500);

    /// <summary>Nombre d'épisodes réellement présents dans Jellyfin, par numéro de saison.</summary>
    public IReadOnlyDictionary<int, int> GetEpisodeCounts(Guid? userId, Guid seriesId)
    {
        var user = userId is { } id ? _users.GetUserById(id) : null;
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { seriesId },
            IsVirtualItem = false,
            Recursive = true
        };
        return _library.GetItemList(query)
            .Where(e => e.ParentIndexNumber is > 0)
            .GroupBy(e => e.ParentIndexNumber!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Lance un rafraîchissement global de la bibliothèque, sans chevauchement.
    /// Le rafraîchissement ciblé par chemin nécessitera la connaissance du chemin final (Spike 2).
    /// </summary>
    public async Task<bool> RefreshAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _refreshRunning, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            _logger.LogInformation("RequestMovies : rafraîchissement de la bibliothèque demandé.");
            await _library.ValidateMediaLibrary(new Progress<double>(), ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            Volatile.Write(ref _refreshRunning, 0);
        }
    }
}
