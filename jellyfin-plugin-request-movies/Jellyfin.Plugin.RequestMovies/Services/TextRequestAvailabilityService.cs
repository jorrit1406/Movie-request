using System;
using System.Linq;
using Jellyfin.Plugin.RequestMovies.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RequestMovies.Services;

public sealed class TextRequestAvailabilityService
{
    private readonly TextRequestStore _requests;
    private readonly JellyfinLibraryService _library;
    private readonly ILogger<TextRequestAvailabilityService> _logger;

    public TextRequestAvailabilityService(
        TextRequestStore requests,
        JellyfinLibraryService library,
        ILogger<TextRequestAvailabilityService> logger)
    {
        _requests = requests;
        _library = library;
        _logger = logger;
    }

    public int Reconcile()
    {
        var availableIds = _requests.GetPendingImdbIds()
            .Where(imdbId => _library.FindByImdb(imdbId).Count > 0)
            .ToArray();
        var completed = _requests.MarkDoneByImdbIds(availableIds);
        if (completed > 0)
        {
            _logger.LogInformation("RequestMovies: automatically marked {Count} IMDb-matched text requests as done.", completed);
        }

        return completed;
    }
}
