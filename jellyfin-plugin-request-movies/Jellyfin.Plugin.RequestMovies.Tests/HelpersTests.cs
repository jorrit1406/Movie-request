using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.RequestMovies.Services;
using Xunit;

namespace Jellyfin.Plugin.RequestMovies.Tests;

public class RateLimiterTests
{
    [Fact]
    public void BlocksAfterLimit_ThenAllowsAfterWindow()
    {
        var limiter = new RateLimiter();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var window = TimeSpan.FromMinutes(1);
        Assert.True(limiter.TryAcquire("u", 2, window, t0));
        Assert.True(limiter.TryAcquire("u", 2, window, t0.AddSeconds(1)));
        Assert.False(limiter.TryAcquire("u", 2, window, t0.AddSeconds(2)));
        Assert.True(limiter.TryAcquire("u", 2, window, t0.AddSeconds(70)));
    }

    [Fact]
    public void IsolatesUsers()
    {
        var limiter = new RateLimiter();
        var t0 = DateTime.UtcNow;
        Assert.True(limiter.TryAcquire("a", 1, TimeSpan.FromMinutes(1), t0));
        Assert.True(limiter.TryAcquire("b", 1, TimeSpan.FromMinutes(1), t0));
    }

    [Fact]
    public void ZeroMeansUnlimited()
    {
        var limiter = new RateLimiter();
        for (var i = 0; i < 100; i++)
        {
            Assert.True(limiter.TryAcquire("u", 0, TimeSpan.FromMinutes(1)));
        }
    }
}

public class AsyncCacheTests
{
    [Fact]
    public async Task ConcurrentCallsShareOneFactoryInvocation()
    {
        var cache = new AsyncCache<int>();
        var calls = 0;
        Func<Task<int>> factory = async () =>
        {
            await Task.Delay(50);
            return System.Threading.Interlocked.Increment(ref calls);
        };

        var results = await Task.WhenAll(
            cache.GetOrAddAsync("k", TimeSpan.FromSeconds(5), factory),
            cache.GetOrAddAsync("k", TimeSpan.FromSeconds(5), factory),
            cache.GetOrAddAsync("k", TimeSpan.FromSeconds(5), factory));

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal(1, r));
    }

    [Fact]
    public async Task FailedFactoryIsNotCached()
    {
        var cache = new AsyncCache<int>();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetOrAddAsync("k", TimeSpan.FromSeconds(5), () => throw new InvalidOperationException()));
        Assert.Equal(7, await cache.GetOrAddAsync("k", TimeSpan.FromSeconds(5), () => Task.FromResult(7)));
    }
}
