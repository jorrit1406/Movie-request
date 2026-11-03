using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jellyfin.Plugin.RequestMovies.Services;

/// <summary>Limiteur à fenêtre glissante, en mémoire, pour freiner les requêtes abusives.</summary>
public sealed class RateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();

    public bool TryAcquire(string key, int max, TimeSpan window) => TryAcquire(key, max, window, DateTime.UtcNow);

    public bool TryAcquire(string key, int max, TimeSpan window, DateTime now)
    {
        if (max <= 0)
        {
            return true; // 0 = illimité
        }

        var queue = _hits.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= max)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
