using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RequestMovies.Seerr;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RequestMovies.Services;

/// <summary>Relie un utilisateur Jellyfin à son compte Seerr (par identifiant Jellyfin, puis par nom).</summary>
public sealed class UserMapper
{
    private readonly SeerrClient _seerr;
    private readonly IUserManager _users;
    private readonly ILogger<UserMapper> _logger;
    private readonly AsyncCache<UserMap> _cache = new();

    public UserMapper(SeerrClient seerr, IUserManager users, ILogger<UserMapper> logger)
    {
        _seerr = seerr;
        _users = users;
        _logger = logger;
    }

    public static string Normalize(string id) => id.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    public async Task<int?> GetSeerrUserIdAsync(Guid jellyfinUserId, bool allowImport, CancellationToken ct)
    {
        var found = await LookupAsync(jellyfinUserId).ConfigureAwait(false);
        if (found is not null || !allowImport)
        {
            return found;
        }

        // Import automatique (une tentative) puis nouvelle lecture.
        try
        {
            var resp = await _seerr.ImportJellyfinUsersAsync(new[] { jellyfinUserId.ToString("N") }, ct).ConfigureAwait(false);
            if (!resp.IsSuccess)
            {
                _logger.LogWarning("RequestMovies : import utilisateur refusé par Seerr (HTTP {Status}).", resp.StatusCode);
                return null;
            }

            _cache.Clear();
            return await LookupAsync(jellyfinUserId).ConfigureAwait(false);
        }
        catch (SeerrUnavailableException)
        {
            return null;
        }
    }

    private async Task<int?> LookupAsync(Guid jellyfinUserId)
    {
        var map = await _cache.GetOrAddAsync("users", TimeSpan.FromMinutes(5), LoadAsync).ConfigureAwait(false);
        if (map.ById.TryGetValue(Normalize(jellyfinUserId.ToString("N")), out var id))
        {
            return id;
        }

        var name = _users.GetUserById(jellyfinUserId)?.Username?.ToLowerInvariant();
        if (name is not null && map.ByName.TryGetValue(name, out id))
        {
            return id;
        }

        return null;
    }

    private async Task<UserMap> LoadAsync()
    {
        var map = new UserMap();
        const int take = 100;
        for (var skip = 0; skip < 5000; skip += take)
        {
            var resp = await _seerr.GetUsersAsync(take, skip, CancellationToken.None).ConfigureAwait(false);
            if (!resp.IsSuccess)
            {
                throw new SeerrUnavailableException("Impossible de lire les utilisateurs Seerr.");
            }

            using var doc = JsonDocument.Parse(resp.Body);
            var results = doc.RootElement.Get("results");
            if (results is not { ValueKind: JsonValueKind.Array } arr)
            {
                break;
            }

            var count = 0;
            foreach (var u in arr.EnumerateArray())
            {
                count++;
                var uid = u.Get("id").Int();
                if (uid is null)
                {
                    continue;
                }

                var jid = u.Get("jellyfinUserId").Str();
                if (!string.IsNullOrEmpty(jid))
                {
                    map.ById[Normalize(jid)] = uid.Value;
                }

                var jname = u.Get("jellyfinUsername").Str() ?? u.Get("username").Str();
                if (!string.IsNullOrEmpty(jname))
                {
                    map.ByName[jname.ToLowerInvariant()] = uid.Value;
                }
            }

            if (count < take)
            {
                break;
            }
        }

        return map;
    }

    private sealed class UserMap
    {
        public Dictionary<string, int> ById { get; } = new();

        public Dictionary<string, int> ByName { get; } = new();
    }
}
