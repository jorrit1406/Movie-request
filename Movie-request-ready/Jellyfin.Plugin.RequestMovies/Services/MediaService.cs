using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RequestMovies.Configuration;
using Jellyfin.Plugin.RequestMovies.Library;
using Jellyfin.Plugin.RequestMovies.Models;
using Jellyfin.Plugin.RequestMovies.Seerr;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RequestMovies.Services;

/// <summary>
/// Cœur métier côté serveur : permissions, recherche à la demande, statuts, doublons, demandes et signalements.
/// Les quotas et l'approbation restent gérés par Seerr.
/// </summary>
public sealed class MediaService
{
    private static readonly Dictionary<string, (string Label, int SeerrType)> ReportCategories = new()
    {
        ["playback"] = ("Erreur de lecture", 1),
        ["nosound"] = ("Absence de son", 2),
        ["avproblem"] = ("Problème audio/vidéo", 1),
        ["subtitles"] = ("Sous-titres absents ou incorrects", 3),
        ["quality"] = ("Mauvaise qualité", 1),
        ["wrongversion"] = ("Mauvaise version du contenu", 4),
        ["wronglanguage"] = ("Langue incorrecte", 2),
        ["wrongitem"] = ("Film ou épisode incorrect", 4),
        ["incomplete"] = ("Contenu incomplet", 4),
        ["other"] = ("Autre problème", 4)
    };

    private readonly SeerrClient _seerr;
    private readonly JellyfinLibraryService _library;
    private readonly UserMapper _mapper;
    private readonly IUserManager _users;
    private readonly ILogger<MediaService> _logger;
    private readonly AsyncCache<string?> _details = new();
    private readonly AsyncCache<string> _searches = new();

    public MediaService(
        SeerrClient seerr,
        JellyfinLibraryService library,
        UserMapper mapper,
        IUserManager users,
        ILogger<MediaService> logger)
    {
        _seerr = seerr;
        _library = library;
        _mapper = mapper;
        _users = users;
        _logger = logger;
    }

    private static PluginConfiguration Cfg => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public static bool IsValidMediaType(string? t) => t is "movie" or "tv";

    // ---------------------------------------------------------------- permissions

    public (bool Allowed, string? Reason) Permission(Guid? userId)
    {
        var cfg = Cfg;
        if (!_seerr.IsConfigured)
        {
            return (false, "Les demandes ne sont pas configurées.");
        }

        if (cfg.RequestsSuspended)
        {
            return (false, "Les nouvelles demandes sont temporairement suspendues.");
        }

        if (userId is { } id)
        {
            var n = UserMapper.Normalize(id.ToString("N"));
            if (cfg.DisabledUserIds.Any(d => UserMapper.Normalize(d) == n))
            {
                return (false, "Les demandes sont désactivées pour votre compte.");
            }
        }

        return (true, null);
    }

    public List<QualityInfo> Qualities()
    {
        var cfg = Cfg;
        var list = new List<QualityInfo> { new() { Id = "hd", Label = cfg.Label1080, Is4K = false } };
        if (cfg.Enable4K)
        {
            list.Add(new QualityInfo { Id = "4k", Label = cfg.Label4K, Is4K = true });
        }

        return list;
    }

    // ---------------------------------------------------------------- détails Seerr

    private async Task<string?> GetDetailsJsonAsync(string mediaType, int tmdbId, CancellationToken ct)
    {
        _ = ct; // la requête mutualisée ne doit pas dépendre de l'annulation d'un seul appelant
        return await _details.GetOrAddAsync(
            $"{mediaType}:{tmdbId}",
            TimeSpan.FromSeconds(Math.Max(1, Cfg.StatusCacheSeconds)),
            async () =>
            {
                var r = mediaType == "tv"
                    ? await _seerr.GetTvAsync(tmdbId, CancellationToken.None).ConfigureAwait(false)
                    : await _seerr.GetMovieAsync(tmdbId, CancellationToken.None).ConfigureAwait(false);
                if (r.StatusCode == 404)
                {
                    return null;
                }

                if (!r.IsSuccess)
                {
                    throw new SeerrUnavailableException($"Seerr a répondu HTTP {r.StatusCode}.");
                }

                return r.Body;
            }).ConfigureAwait(false);
    }

    private static Dictionary<bool, int> LatestRequestStatus(JsonElement? mediaInfo)
    {
        var result = new Dictionary<bool, int>();
        var latestId = new Dictionary<bool, int>();
        if (mediaInfo.Get("requests") is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var r in arr.EnumerateArray())
            {
                var id = r.Get("id").Int() ?? 0;
                var st = r.Get("status").Int();
                var is4k = r.Get("is4k").Bool();
                if (st is null)
                {
                    continue;
                }

                if (!latestId.TryGetValue(is4k, out var prev) || id >= prev)
                {
                    latestId[is4k] = id;
                    result[is4k] = st.Value;
                }
            }
        }

        return result;
    }

    // ---------------------------------------------------------------- statut d'un média

    public async Task<MediaStateResponse> GetStateAsync(Guid? userId, string mediaType, int tmdbId, CancellationToken ct)
    {
        var (allowed, reason) = Permission(userId);
        var response = new MediaStateResponse
        {
            MediaType = mediaType,
            TmdbId = tmdbId,
            RequestsAllowed = allowed,
            BlockedReason = reason
        };

        string? json = null;
        if (_seerr.IsConfigured)
        {
            try
            {
                json = await GetDetailsJsonAsync(mediaType, tmdbId, ct).ConfigureAwait(false);
            }
            catch (SeerrUnavailableException ex)
            {
                _logger.LogWarning("RequestMovies : Seerr indisponible pour le statut ({Message}).", ex.Message);
                response.SeerrAvailable = false;
            }
        }

        var seerrOk = response.SeerrAvailable && _seerr.IsConfigured;
        var items = _library.FindByTmdb(userId, mediaType, tmdbId);
        using var doc = json is null ? null : JsonDocument.Parse(json);
        JsonElement? root = doc?.RootElement;
        var mediaInfo = root.Get("mediaInfo");
        var latest = LatestRequestStatus(mediaInfo);

        IReadOnlyDictionary<int, int> episodeCounts = new Dictionary<int, int>();
        if (mediaType == "tv" && items.Count > 0)
        {
            episodeCounts = _library.GetEpisodeCounts(userId, items[0].Id);
        }

        foreach (var q in Qualities())
        {
            var seerrMediaStatus = (q.Is4K ? mediaInfo.Get("status4k") : mediaInfo.Get("status")).Int();
            latest.TryGetValue(q.Is4K, out var latestReq);
            int? latestReqN = latest.ContainsKey(q.Is4K) ? latestReq : null;
            var dto = new QualityStateDto { Id = q.Id, Label = q.Label, Is4K = q.Is4K };

            MediaState state;
            if (mediaType == "tv")
            {
                var seasonStates = new List<MediaState>();
                dto.Seasons = new List<SeasonStateDto>();
                var seasonStatus = new Dictionary<int, int>();
                if (mediaInfo.Get("seasons") is { ValueKind: JsonValueKind.Array } ms)
                {
                    foreach (var s in ms.EnumerateArray())
                    {
                        var n = s.Get("seasonNumber").Int();
                        var st = (q.Is4K ? s.Get("status4k") : s.Get("status")).Int();
                        if (n is not null && st is not null)
                        {
                            seasonStatus[n.Value] = st.Value;
                        }
                    }
                }

                if (root.Get("seasons") is { ValueKind: JsonValueKind.Array } seasons)
                {
                    foreach (var s in seasons.EnumerateArray())
                    {
                        var n = s.Get("seasonNumber").Int() ?? 0;
                        if (n <= 0)
                        {
                            continue;
                        }

                        var epCount = s.Get("episodeCount").Int() ?? 0;
                        episodeCounts.TryGetValue(n, out var jf);
                        seasonStatus.TryGetValue(n, out var seerrSeason);
                        int? seerrSeasonN = seasonStatus.ContainsKey(n) ? seerrSeason : null;

                        // 4K : on ne connaît pas la résolution par épisode ; on s'appuie sur Seerr pour ce niveau.
                        var jellyfinEpisodes = q.Is4K && seerrSeasonN is not (4 or 5) ? 0 : jf;
                        var ss = MediaStateResolver.ResolveSeason(jellyfinEpisodes, epCount, seerrSeasonN);
                        seasonStates.Add(ss);
                        dto.Seasons.Add(new SeasonStateDto
                        {
                            Number = n,
                            State = MediaStateResolver.Key(ss),
                            StateLabel = MediaStateResolver.Label(ss),
                            CanRequest = seerrOk && allowed && MediaStateResolver.CanRequest(ss),
                            Episodes = jf,
                            EpisodeCount = epCount
                        });
                    }
                }

                state = MediaStateResolver.ResolveSeries(seasonStates, seerrMediaStatus, latestReqN);
                dto.CanRequest = seerrOk && allowed
                    && (dto.Seasons.Count > 0 ? dto.Seasons.Any(s => s.CanRequest) : MediaStateResolver.CanRequest(state));
            }
            else
            {
                var inJellyfin = q.Is4K ? JellyfinLibraryService.Has4K(items) : items.Count > 0;
                state = MediaStateResolver.ResolveMovie(inJellyfin, seerrMediaStatus, latestReqN);
                dto.CanRequest = seerrOk && allowed && MediaStateResolver.CanRequest(state);
            }

            dto.State = MediaStateResolver.Key(state);
            dto.StateLabel = MediaStateResolver.Label(state);
            response.Qualities.Add(dto);
        }

        var primary = response.Qualities[0];
        response.State = primary.State;
        response.StateLabel = primary.StateLabel;
        return response;
    }

    // ---------------------------------------------------------------- recherche « à la demande »

    public async Task<SearchResponse> SearchAsync(Guid? userId, string query, int page, CancellationToken ct)
    {
        var (allowed, reason) = Permission(userId);
        var response = new SearchResponse
        {
            Page = page,
            RequestsAllowed = allowed,
            BlockedReason = reason,
            Qualities = Qualities()
        };

        if (!_seerr.IsConfigured)
        {
            response.SeerrAvailable = false;
            return response;
        }

        string body;
        try
        {
            body = await _searches.GetOrAddAsync(
                $"{query.ToLowerInvariant()}|{page}",
                TimeSpan.FromSeconds(Math.Max(1, Cfg.SearchCacheSeconds)),
                async () =>
                {
                    var r = await _seerr.SearchAsync(query, page, CancellationToken.None).ConfigureAwait(false);
                    if (!r.IsSuccess)
                    {
                        throw new SeerrUnavailableException($"Seerr a répondu HTTP {r.StatusCode}.");
                    }

                    return r.Body;
                }).ConfigureAwait(false);
        }
        catch (SeerrUnavailableException ex)
        {
            _logger.LogWarning("RequestMovies : recherche Seerr indisponible ({Message}).", ex.Message);
            response.SeerrAvailable = false;
            return response;
        }

        using var doc = JsonDocument.Parse(body);
        JsonElement? root = doc.RootElement;
        response.TotalPages = root.Get("totalPages").Int() ?? 1;
        if (root.Get("results") is not { ValueKind: JsonValueKind.Array } results)
        {
            return response;
        }

        foreach (var r in results.EnumerateArray())
        {
            if (response.Results.Count >= 12)
            {
                break;
            }

            var type = r.Get("mediaType").Str();
            var tmdbId = r.Get("id").Int();
            if (!IsValidMediaType(type) || tmdbId is null)
            {
                continue;
            }

            var mediaInfo = r.Get("mediaInfo");
            var seerrStatus = mediaInfo.Get("status").Int();
            var latest = LatestRequestStatus(mediaInfo);
            int? latestReq = latest.TryGetValue(false, out var lr) ? lr : null;
            var items = _library.FindByTmdb(userId, type!, tmdbId.Value);

            MediaState state;
            if (type == "tv")
            {
                if (items.Count > 0)
                {
                    if (seerrStatus == 5)
                    {
                        continue; // entièrement disponible : visible dans la bibliothèque
                    }

                    state = MediaState.Partial;
                }
                else
                {
                    state = MediaStateResolver.ResolveMovie(false, seerrStatus, latestReq);
                }
            }
            else
            {
                state = MediaStateResolver.ResolveMovie(items.Count > 0, seerrStatus, latestReq);
                if (state == MediaState.Available)
                {
                    continue;
                }
            }

            var date = r.Get("releaseDate").Str() ?? r.Get("firstAirDate").Str();
            response.Results.Add(new SearchItemDto
            {
                MediaType = type!,
                TmdbId = tmdbId.Value,
                Title = r.Get("title").Str() ?? r.Get("name").Str() ?? string.Empty,
                Year = date is { Length: >= 4 } ? date[..4] : null,
                Overview = Truncate(r.Get("overview").Str(), 400),
                Poster = r.Get("posterPath").Str(),
                State = MediaStateResolver.Key(state),
                StateLabel = MediaStateResolver.Label(state),
                CanRequest = allowed && MediaStateResolver.CanRequest(state)
            });
        }

        return response;
    }

    // ---------------------------------------------------------------- demande

    public async Task<ActionOutcome> RequestAsync(Guid userId, RequestBody body, CancellationToken ct)
    {
        var (allowed, reason) = Permission(userId);
        if (!allowed)
        {
            return Fail(403, reason ?? "Demandes non autorisées.");
        }

        if (!IsValidMediaType(body.MediaType) || body.TmdbId <= 0)
        {
            return Fail(400, "Média invalide.");
        }

        var quality = Qualities().FirstOrDefault(q => q.Id == body.Quality);
        if (quality is null)
        {
            return Fail(400, "Qualité non proposée.");
        }

        _details.Remove($"{body.MediaType}:{body.TmdbId}");
        MediaStateResponse state;
        try
        {
            state = await GetStateAsync(userId, body.MediaType, body.TmdbId, ct).ConfigureAwait(false);
        }
        catch (SeerrUnavailableException)
        {
            return Fail(502, "Seerr est momentanément indisponible.");
        }

        if (!state.SeerrAvailable)
        {
            return Fail(502, "Seerr est momentanément indisponible.");
        }

        var qs = state.Qualities.First(q => q.Id == quality.Id);
        if (!qs.CanRequest)
        {
            return Fail(409, qs.State == "available"
                ? "Ce contenu est déjà disponible dans cette qualité."
                : "Ce contenu est déjà demandé ou en cours de traitement.");
        }

        List<int>? seasons = null;
        if (body.MediaType == "tv")
        {
            var requestable = qs.Seasons?.Where(s => s.CanRequest).Select(s => s.Number).ToList() ?? new List<int>();
            seasons = body.Seasons is { Count: > 0 } ? body.Seasons.Where(requestable.Contains).Distinct().ToList() : requestable;
            if (seasons.Count == 0)
            {
                return Fail(409, "Aucune saison à demander : elles sont déjà disponibles ou demandées.");
            }
        }

        var seerrUser = await _mapper.GetSeerrUserIdAsync(userId, Cfg.AutoImportUsers, ct).ConfigureAwait(false);
        if (seerrUser is null)
        {
            return Fail(403, "Votre compte n’existe pas dans Seerr. Demandez à un administrateur d’importer les utilisateurs Jellyfin.");
        }

        var cfg = Cfg;
        SeerrResponse resp;
        try
        {
            resp = await _seerr.CreateRequestAsync(
                body.MediaType,
                body.TmdbId,
                quality.Is4K,
                seasons,
                seerrUser.Value,
                quality.Is4K ? cfg.ServerId4K : cfg.ServerId1080,
                quality.Is4K ? cfg.ProfileId4K : cfg.ProfileId1080,
                ct).ConfigureAwait(false);
        }
        catch (SeerrUnavailableException)
        {
            return Fail(502, "Seerr est momentanément indisponible.");
        }

        _details.Remove($"{body.MediaType}:{body.TmdbId}");
        switch (resp.StatusCode)
        {
            case 200:
            case 201:
                return new ActionOutcome { Ok = true, Message = "Demande envoyée.", HttpStatus = 200 };
            case 202:
                return Fail(409, "Rien à demander : tout est déjà demandé ou disponible.");
            case 403:
                return Fail(403, "Seerr a refusé la demande (quota atteint ou permission insuffisante)" + Suffix(resp.Body));
            case 409:
                return Fail(409, "Ce contenu est déjà demandé.");
            default:
                _logger.LogWarning("RequestMovies : Seerr a répondu HTTP {Status} à la demande.", resp.StatusCode);
                return Fail(502, $"Seerr a répondu HTTP {resp.StatusCode}.");
        }
    }

    // ---------------------------------------------------------------- signalement

    public async Task<ActionOutcome> ReportAsync(Guid userId, ReportBody body, CancellationToken ct)
    {
        if (!_seerr.IsConfigured)
        {
            return Fail(503, "Les signalements ne sont pas configurés.");
        }

        if (!IsValidMediaType(body.MediaType) || body.TmdbId <= 0)
        {
            return Fail(400, "Média invalide.");
        }

        if (!ReportCategories.TryGetValue(body.Category ?? string.Empty, out var cat))
        {
            return Fail(400, "Catégorie de signalement inconnue.");
        }

        int? mediaId;
        try
        {
            var json = await GetDetailsJsonAsync(body.MediaType, body.TmdbId, ct).ConfigureAwait(false);
            if (json is null)
            {
                return Fail(404, "Ce média est inconnu de Seerr.");
            }

            using var doc = JsonDocument.Parse(json);
            mediaId = doc.RootElement.Get("mediaInfo").Get("id").Int();
        }
        catch (SeerrUnavailableException)
        {
            return Fail(502, "Seerr est momentanément indisponible.");
        }

        if (mediaId is null)
        {
            return Fail(404, "Ce média n’est pas encore enregistré dans Seerr : le signalement est impossible pour le moment.");
        }

        var name = _users.GetUserById(userId)?.Username ?? "utilisateur Jellyfin";
        var sb = new StringBuilder();
        sb.Append('[').Append(cat.Label).Append(']');
        if (body.Season is > 0)
        {
            sb.Append(" Saison ").Append(body.Season.Value.ToString(CultureInfo.InvariantCulture));
            if (body.Episode is > 0)
            {
                sb.Append(", épisode ").Append(body.Episode.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        var comment = Sanitize(body.Comment, 1000);
        if (comment.Length > 0)
        {
            sb.Append('\n').Append(comment);
        }

        sb.Append("\n— signalé depuis Jellyfin par ").Append(name);

        try
        {
            var resp = await _seerr.CreateIssueAsync(cat.SeerrType, sb.ToString(), mediaId.Value, body.Season, body.Episode, ct)
                .ConfigureAwait(false);
            return resp.IsSuccess
                ? new ActionOutcome { Ok = true, Message = "Merci, le signalement a été transmis." }
                : Fail(502, $"Seerr a répondu HTTP {resp.StatusCode}.");
        }
        catch (SeerrUnavailableException)
        {
            return Fail(502, "Seerr est momentanément indisponible.");
        }
    }

    // ---------------------------------------------------------------- utilitaires

    private static ActionOutcome Fail(int status, string message)
        => new() { Ok = false, Message = message, HttpStatus = status };

    private static string Suffix(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var msg = doc.RootElement.Get("message").Str();
            return string.IsNullOrWhiteSpace(msg) ? "." : " : " + Truncate(msg, 200);
        }
        catch (JsonException)
        {
            return ".";
        }
    }

    private static string? Truncate(string? s, int max)
        => s is null ? null : s.Length <= max ? s : s[..max] + "…";

    private static string Sanitize(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var cleaned = new string(s.Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        return cleaned.Length <= max ? cleaned : cleaned[..max];
    }
}
