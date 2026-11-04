using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.RequestMovies.Services;

/// <summary>Cache à expiration avec déduplication des requêtes concurrentes (une seule fabrique par clé).</summary>
public sealed class AsyncCache<T>
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public async Task<T> GetOrAddAsync(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        var now = DateTime.UtcNow;
        var entry = _entries.AddOrUpdate(
            key,
            _ => new Entry(now + ttl, new Lazy<Task<T>>(factory)),
            (_, existing) => existing.Expires > now ? existing : new Entry(now + ttl, new Lazy<Task<T>>(factory)));
        try
        {
            return await entry.Value.Value.ConfigureAwait(false);
        }
        catch
        {
            _entries.TryRemove(key, out _);
            throw;
        }
    }

    public void Remove(string key) => _entries.TryRemove(key, out _);

    public void Clear() => _entries.Clear();

    private sealed record Entry(DateTime Expires, Lazy<Task<T>> Value);
}
