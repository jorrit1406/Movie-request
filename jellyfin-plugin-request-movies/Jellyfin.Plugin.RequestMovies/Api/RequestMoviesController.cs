using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RequestMovies.Models;
using Jellyfin.Plugin.RequestMovies.Seerr;
using Jellyfin.Plugin.RequestMovies.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RequestMovies.Api;

/// <summary>
/// Endpoints du plugin. Toute autorisation est vérifiée ici, côté serveur : l'interface n'est jamais une source de confiance.
/// </summary>
[ApiController]
[Route("RequestMovies")]
[Produces("application/json")]
public class RequestMoviesController : ControllerBase
{
    private static readonly Regex PosterPath = new(@"^/[A-Za-z0-9_-]{5,64}\.(jpg|png)$", RegexOptions.Compiled);
    private static readonly Regex ImdbIdPattern = new(@"^tt\d{7,10}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ImdbPathPattern = new(@"^/title/(tt\d{7,10})/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly SeerrClient _seerr;
    private readonly MediaService _media;
    private readonly AvailabilityService _availability;
    private readonly RateLimiter _limiter;
    private readonly IHttpClientFactory _http;
    private readonly TextRequestStore _textRequests;
    private readonly TextRequestAvailabilityService _textRequestAvailability;
    private readonly IUserManager _users;

    public RequestMoviesController(
        SeerrClient seerr,
        MediaService media,
        AvailabilityService availability,
        RateLimiter limiter,
        IHttpClientFactory http,
        TextRequestStore textRequests,
        TextRequestAvailabilityService textRequestAvailability,
        IUserManager users)
    {
        _seerr = seerr;
        _media = media;
        _availability = availability;
        _limiter = limiter;
        _http = http;
        _textRequests = textRequests;
        _textRequestAvailability = textRequestAvailability;
        _users = users;
    }

    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var id) ? id : null;

    private static int Limit(int configured) => configured;

    private static bool TextOnlyMode => Plugin.Instance?.Configuration.TextOnlyMode == true;

    private static bool TryParseImdbId(string? value, out string? imdbId)
    {
        imdbId = null;
        var input = value?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            return true;
        }

        if (ImdbIdPattern.IsMatch(input))
        {
            imdbId = input.ToLowerInvariant();
            return true;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || (uri.Host != "imdb.com" && uri.Host != "www.imdb.com" && uri.Host != "m.imdb.com"))
        {
            return false;
        }

        var match = ImdbPathPattern.Match(uri.AbsolutePath);
        if (!match.Success)
        {
            return false;
        }

        imdbId = match.Groups[1].Value.ToLowerInvariant();
        return true;
    }

    // ------------------------------------------------------------------ utilisateur authentifié

    [HttpGet("Ping")]
    [Authorize]
    public ActionResult<object> Ping() => Ok(new { plugin = "RequestMovies", ok = true });

    [HttpGet("Mode")]
    [Authorize]
    public ActionResult<object> Mode() => Ok(new { textOnlyMode = TextOnlyMode });

    [HttpGet("TextRequests")]
    [Authorize]
    public ActionResult GetTextRequests()
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        _textRequestAvailability.Reconcile();
        return Ok(_textRequests.GetAll(userId.Value));
    }

    [HttpPost("TextRequests")]
    [Authorize]
    public ActionResult AddTextRequest([FromBody] CreateTextRequestBody body)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var title = (body.Title ?? string.Empty).Trim();
        var notes = (body.Notes ?? string.Empty).Trim();
        if (title.Length is < 1 or > 120 || notes.Length > 500)
        {
            return BadRequest(new { message = "The title must be 1 to 120 characters and notes must be 500 characters or fewer." });
        }

        if ((body.ImdbUrl?.Length ?? 0) > 300 || !TryParseImdbId(body.ImdbUrl, out var imdbId))
        {
            return BadRequest(new { message = "Enter a valid IMDb title link or title ID, such as tt0133093." });
        }

        if (!_limiter.TryAcquire($"text-request:{userId}", 5, TimeSpan.FromMinutes(1)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many requests. Please wait a moment." });
        }

        var username = _users.GetUserById(userId.Value)?.Username ?? "Jellyfin";
        return Ok(_textRequests.Add(title, notes, username, imdbId));
    }

    [HttpPost("TextRequests/{id:guid}/Vote")]
    [Authorize]
    public ActionResult VoteTextRequest(Guid id)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!_limiter.TryAcquire($"text-vote:{userId}", 30, TimeSpan.FromMinutes(1)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many votes. Please wait a moment." });
        }

        var request = _textRequests.ToggleVote(id, userId.Value);
        return request is null ? NotFound() : Ok(request);
    }

    [HttpDelete("TextRequests/{id:guid}")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult DeleteTextRequest(Guid id)
    {
        return _textRequests.Delete(id) ? Ok(new { ok = true }) : NotFound();
    }

    [HttpPost("TextRequests/{id:guid}/Done")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult MarkTextRequestDone(Guid id)
    {
        return _textRequests.MarkDone(id) ? Ok(new { ok = true }) : NotFound();
    }

    /// <summary>Résultats « disponible à la demande » (absents de la bibliothèque).</summary>
    [HttpGet("Search")]
    [Authorize]
    public async Task<ActionResult> Search([FromQuery] string query, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (TextOnlyMode)
        {
            return Conflict(new { message = "External search is disabled in text-only mode." });
        }

        var q = (query ?? string.Empty).Trim();
        if (q.Length is < 2 or > 100)
        {
            return BadRequest(new { message = "Invalid query." });
        }

        if (!_limiter.TryAcquire($"search:{userId}", Limit(Plugin.Instance!.Configuration.SearchPerMinute), TimeSpan.FromMinutes(1)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many searches. Please wait a moment." });
        }

        return Ok(await _media.SearchAsync(userId, q, Math.Clamp(page, 1, 20), ct).ConfigureAwait(false));
    }

    /// <summary>Statut d'un film ou d'une série sur sa fiche, par qualité et par saison.</summary>
    [HttpGet("State")]
    [Authorize]
    public async Task<ActionResult> State([FromQuery] string mediaType, [FromQuery] int tmdbId, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (TextOnlyMode)
        {
            return Conflict(new { message = "External statuses are disabled in text-only mode." });
        }

        if (!MediaService.IsValidMediaType(mediaType) || tmdbId <= 0)
        {
            return BadRequest(new { message = "Invalid media." });
        }

        if (!_limiter.TryAcquire($"state:{userId}", 120, TimeSpan.FromMinutes(1)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many requests." });
        }

        return Ok(await _media.GetStateAsync(userId, mediaType, tmdbId, ct).ConfigureAwait(false));
    }

    [HttpPost("Request")]
    [Authorize]
    public async Task<ActionResult> CreateRequest([FromBody] RequestBody body, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (TextOnlyMode)
        {
            return Conflict(new { message = "Seerr requests are disabled in text-only mode." });
        }

        if (!_limiter.TryAcquire($"request:{userId}", Limit(Plugin.Instance!.Configuration.RequestsPerMinute), TimeSpan.FromMinutes(1)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { ok = false, message = "Too many requests. Please wait a moment." });
        }

        var result = await _media.RequestAsync(userId.Value, body, ct).ConfigureAwait(false);
        return StatusCode(result.HttpStatus, result);
    }

    [HttpPost("Report")]
    [Authorize]
    public async Task<ActionResult> Report([FromBody] ReportBody body, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (TextOnlyMode)
        {
            return Conflict(new { message = "Seerr reports are disabled in text-only mode." });
        }

        if (!_limiter.TryAcquire($"report:{userId}", 5, TimeSpan.FromMinutes(1)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { ok = false, message = "Too many reports. Please wait a moment." });
        }

        var result = await _media.ReportAsync(userId.Value, body, ct).ConfigureAwait(false);
        return StatusCode(result.HttpStatus, result);
    }

    // ------------------------------------------------------------------ ressources publiques (sans clé ni donnée utilisateur)

    /// <summary>Script client injecté dans Jellyfin Web. Ne contient aucun secret.</summary>
    [HttpGet("client.js")]
    [AllowAnonymous]
    [Produces("text/javascript")]
    public ActionResult ClientScript()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Jellyfin.Plugin.RequestMovies.Web.client.js");
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        return Content(reader.ReadToEnd(), "text/javascript", Encoding.UTF8);
    }

    /// <summary>Proxy d'affiches TMDB : le navigateur n'a jamais besoin d'atteindre un autre site.</summary>
    [HttpGet("Poster")]
    [AllowAnonymous]
    public async Task<ActionResult> Poster([FromQuery] string path, CancellationToken ct = default)
    {
        if (TextOnlyMode)
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(path) || !PosterPath.IsMatch(path))
        {
            return BadRequest();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_limiter.TryAcquire($"poster:{ip}", 240, TimeSpan.FromMinutes(1)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var client = _http.CreateClient("RequestMovies.Tmdb");
            client.Timeout = TimeSpan.FromSeconds(8);
            using var resp = await client.GetAsync("https://image.tmdb.org/t/p/w342" + path, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return NotFound();
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length > 2_000_000)
            {
                return StatusCode(StatusCodes.Status502BadGateway);
            }

            Response.Headers["Cache-Control"] = "public, max-age=86400";
            return File(bytes, path.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Webhook Seerr (facultatif) : accélère la détection ; la vérification périodique reste le filet de sécurité.</summary>
    [HttpPost("Webhook")]
    [AllowAnonymous]
    public ActionResult Webhook([FromQuery] string secret)
    {
        if (TextOnlyMode)
        {
            return NotFound();
        }

        var expected = Plugin.Instance?.Configuration.WebhookSecret ?? string.Empty;
        if (expected.Length < 16)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!_limiter.TryAcquire("webhook", 60, TimeSpan.FromMinutes(1)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var a = Encoding.UTF8.GetBytes(secret ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(expected);
        if (!CryptographicOperations.FixedTimeEquals(a, b))
        {
            return Unauthorized();
        }

        _availability.Nudge();
        return NoContent();
    }

    // ------------------------------------------------------------------ administration

    [HttpGet("SeerrStatus")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<ActionResult> SeerrStatus(CancellationToken ct = default)
    {
        if (TextOnlyMode)
        {
            return Conflict(new { message = "Seerr is disabled in text-only mode." });
        }

        try
        {
            var r = await _seerr.GetStatusAsync(ct).ConfigureAwait(false);
            return new ContentResult { StatusCode = r.StatusCode, Content = r.Body, ContentType = "application/json" };
        }
        catch (SeerrUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }

    [HttpPost("Admin/Sync")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<ActionResult> AdminSync(CancellationToken ct = default)
    {
        if (TextOnlyMode)
        {
            return Conflict(new { message = "Seerr synchronization is disabled in text-only mode." });
        }

        var message = await _availability.ReconcileAsync(true, ct).ConfigureAwait(false);
        return Ok(new { message });
    }

    [HttpGet("Admin/Stuck")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult AdminStuck() => Ok(_availability.GetStuck());
}
