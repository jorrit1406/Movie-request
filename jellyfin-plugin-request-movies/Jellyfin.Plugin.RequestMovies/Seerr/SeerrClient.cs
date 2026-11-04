using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.RequestMovies.Seerr;

/// <summary>Réponse brute de Seerr : le code HTTP compte (201, 202, 403, 409...).</summary>
public sealed record SeerrResponse(int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>Seerr injoignable ou mal configuré.</summary>
public sealed class SeerrUnavailableException : Exception
{
    public SeerrUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Client de l'API Seerr. Toutes les requêtes passent par le serveur : la clé API n'est jamais exposée.
/// Renvoie le code HTTP et le corps bruts ; les erreurs réseau deviennent des SeerrUnavailableException.
/// </summary>
public class SeerrClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SeerrClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public bool IsConfigured
    {
        get
        {
            var c = Plugin.Instance?.Configuration;
            return c is not null && !c.TextOnlyMode
                && !string.IsNullOrWhiteSpace(c.SeerrUrl)
                && !string.IsNullOrWhiteSpace(c.SeerrApiKey);
        }
    }

    private HttpClient CreateClient()
    {
        var config = Plugin.Instance?.Configuration
            ?? throw new SeerrUnavailableException("Plugin non initialisé.");
        if (!IsConfigured)
        {
            throw new SeerrUnavailableException("Seerr n’est pas configuré.");
        }

        var client = _httpClientFactory.CreateClient("RequestMovies.Seerr");
        client.BaseAddress = new Uri(config.SeerrUrl.TrimEnd('/') + "/api/v1/");
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Add("X-Api-Key", config.SeerrApiKey);
        return client;
    }

    private async Task<SeerrResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new SeerrResponse((int)response.StatusCode, text);
        }
        catch (SeerrUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            // Le message ne contient jamais la clé API (elle est dans un en-tête, pas dans l'URL).
            throw new SeerrUnavailableException("Seerr est momentanément indisponible.", ex);
        }
    }

    public Task<SeerrResponse> GetStatusAsync(CancellationToken ct)
        => SendAsync(HttpMethod.Get, "status", null, ct);

    public Task<SeerrResponse> SearchAsync(string query, int page, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"search?query={Uri.EscapeDataString(query)}&page={page}", null, ct);

    public Task<SeerrResponse> GetMovieAsync(int tmdbId, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"movie/{tmdbId}", null, ct);

    public Task<SeerrResponse> GetTvAsync(int tmdbId, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"tv/{tmdbId}", null, ct);

    public Task<SeerrResponse> GetUsersAsync(int take, int skip, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"user?take={take}&skip={skip}", null, ct);

    public Task<SeerrResponse> ImportJellyfinUsersAsync(IEnumerable<string> jellyfinUserIds, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "user/import-from-jellyfin", new { jellyfinUserIds }, ct);

    public Task<SeerrResponse> GetRequestsAsync(string filter, int take, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"request?take={take}&skip=0&filter={Uri.EscapeDataString(filter)}&sort=modified", null, ct);

    /// <summary>Crée une demande au nom d'un utilisateur Seerr (champ userId).</summary>
    public Task<SeerrResponse> CreateRequestAsync(
        string mediaType,
        int tmdbId,
        bool is4k,
        IReadOnlyList<int>? seasons,
        int seerrUserId,
        int serverId,
        int profileId,
        CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["mediaType"] = mediaType,
            ["mediaId"] = tmdbId,
            ["is4k"] = is4k,
            ["userId"] = seerrUserId
        };
        if (seasons is { Count: > 0 })
        {
            body["seasons"] = seasons;
        }

        if (serverId > 0)
        {
            body["serverId"] = serverId;
        }

        if (profileId > 0)
        {
            body["profileId"] = profileId;
        }

        return SendAsync(HttpMethod.Post, "request", body, ct);
    }

    /// <summary>Crée une Issue ; seerrMediaId est l'identifiant interne Seerr (pas l'ID TMDB).</summary>
    public Task<SeerrResponse> CreateIssueAsync(
        int issueType, string message, int seerrMediaId, int? season, int? episode, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["issueType"] = issueType,
            ["message"] = message,
            ["mediaId"] = seerrMediaId
        };
        if (season is > 0)
        {
            body["problemSeason"] = season.Value;
        }

        if (episode is > 0)
        {
            body["problemEpisode"] = episode.Value;
        }

        return SendAsync(HttpMethod.Post, "issue", body, ct);
    }
}
