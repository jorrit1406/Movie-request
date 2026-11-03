using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RequestMovies.Configuration;
using Jellyfin.Plugin.RequestMovies.Library;
using Jellyfin.Plugin.RequestMovies.Models;
using Jellyfin.Plugin.RequestMovies.Seerr;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RequestMovies.Services;

/// <summary>
/// Disponibilité réelle : détecte les médias que Seerr dit disponibles mais que Jellyfin n'indexe pas encore,
/// puis déclenche un rafraîchissement contrôlé (regroupé, espacé, borné en tentatives et en durée).
/// Idempotent : webhook et vérification périodique convergent vers le même traitement centralisé.
/// </summary>
public sealed class AvailabilityService : IDisposable
{
    private readonly SeerrClient _seerr;
    private readonly JellyfinLibraryService _library;
    private readonly ILogger<AvailabilityService> _logger;
    private readonly ConcurrentDictionary<string, Entry> _pending = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _timerLock = new();
    private Timer? _debounce;
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    public AvailabilityService(SeerrClient seerr, JellyfinLibraryService library, ILogger<AvailabilityService> logger)
    {
        _seerr = seerr;
        _library = library;
        _logger = logger;
    }

    private static PluginConfiguration Cfg => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>Signale qu'un événement (webhook) a eu lieu : planifie une vérification après un court délai (regroupement).</summary>
    public void Nudge()
    {
        lock (_timerLock)
        {
            _debounce?.Dispose();
            _debounce = new Timer(
                _ => _ = RunNudgedAsync(),
                null,
                TimeSpan.FromSeconds(20),
                Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RunNudgedAsync()
    {
        try
        {
            await ReconcileAsync(false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RequestMovies : vérification déclenchée par webhook en échec.");
        }
    }

    public IReadOnlyList<StuckItemDto> GetStuck()
    {
        var cfg = Cfg;
        var maxWait = TimeSpan.FromHours(Math.Max(1, cfg.MaxWaitHours));
        var now = DateTime.UtcNow;
        return _pending
            .Where(kv => kv.Value.Attempts >= Math.Max(1, cfg.MaxRefreshAttempts) || now - kv.Value.FirstSeenUtc >= maxWait)
            .Select(kv => new StuckItemDto
            {
                Key = kv.Key,
                Attempts = kv.Value.Attempts,
                FirstSeenUtc = kv.Value.FirstSeenUtc.ToString("O")
            })
            .ToList();
    }

    /// <summary>Vérifie les demandes terminées côté Seerr et rafraîchit la bibliothèque si nécessaire.</summary>
    public async Task<string> ReconcileAsync(bool force, CancellationToken ct)
    {
        if (!_seerr.IsConfigured)
        {
            return "Seerr n’est pas configuré.";
        }

        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return "Une vérification est déjà en cours.";
        }

        try
        {
            var cfg = Cfg;
            var candidates = new HashSet<string>();

            SeerrResponse resp;
            try
            {
                resp = await _seerr.GetRequestsAsync("available", 50, ct).ConfigureAwait(false);
            }
            catch (SeerrUnavailableException ex)
            {
                _logger.LogWarning("RequestMovies : Seerr indisponible pendant la vérification ({Message}).", ex.Message);
                return "Seerr indisponible.";
            }

            if (!resp.IsSuccess)
            {
                return $"Seerr a répondu HTTP {resp.StatusCode}.";
            }

            using (var doc = JsonDocument.Parse(resp.Body))
            {
                if (doc.RootElement.Get("results") is { ValueKind: JsonValueKind.Array } results)
                {
                    foreach (var r in results.EnumerateArray())
                    {
                        var media = r.Get("media");
                        var type = media.Get("mediaType").Str() ?? r.Get("type").Str();
                        var tmdb = media.Get("tmdbId").Int();
                        var status = media.Get("status").Int();
                        if (!MediaService.IsValidMediaType(type) || tmdb is null || status is not (4 or 5))
                        {
                            continue;
                        }

                        if (_library.FindByTmdb(null, type!, tmdb.Value).Count == 0)
                        {
                            candidates.Add($"{type}:{tmdb.Value}");
                        }
                    }
                }
            }

            // Les éléments désormais indexés (ou plus candidats) sortent du suivi.
            foreach (var key in _pending.Keys.Where(k => !candidates.Contains(k)).ToList())
            {
                _pending.TryRemove(key, out _);
            }

            foreach (var key in candidates)
            {
                _pending.TryAdd(key, new Entry(DateTime.UtcNow));
            }

            var now = DateTime.UtcNow;
            var maxAttempts = Math.Max(1, cfg.MaxRefreshAttempts);
            var maxWait = TimeSpan.FromHours(Math.Max(1, cfg.MaxWaitHours));
            var eligible = candidates
                .Where(k => _pending.TryGetValue(k, out var e) && e.Attempts < maxAttempts && now - e.FirstSeenUtc < maxWait)
                .ToList();

            if (eligible.Count == 0)
            {
                return candidates.Count == 0
                    ? "Rien à rafraîchir."
                    : $"{candidates.Count} média(s) en attente d’indexation ; limites atteintes, intervention manuelle possible.";
            }

            var minInterval = TimeSpan.FromMinutes(Math.Max(1, cfg.MinRefreshIntervalMinutes));
            if (!force && now - _lastRefreshUtc < minInterval)
            {
                return "Rafraîchissement récent : en attente du prochain créneau.";
            }

            if (_library.RefreshRunning)
            {
                return "Un rafraîchissement de la bibliothèque est déjà en cours.";
            }

            _lastRefreshUtc = DateTime.UtcNow;
            foreach (var key in eligible)
            {
                if (_pending.TryGetValue(key, out var e))
                {
                    e.Attempts++;
                }
            }

            // Un seul rafraîchissement global pour tous les médias concernés.
            var started = await _library.RefreshAsync(ct).ConfigureAwait(false);
            return started
                ? $"Rafraîchissement lancé pour {eligible.Count} média(s)."
                : "Un rafraîchissement est déjà en cours.";
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        lock (_timerLock)
        {
            _debounce?.Dispose();
        }

        _gate.Dispose();
    }

    private sealed class Entry
    {
        public Entry(DateTime firstSeenUtc)
        {
            FirstSeenUtc = firstSeenUtc;
        }

        public DateTime FirstSeenUtc { get; }

        public int Attempts { get; set; }
    }
}
