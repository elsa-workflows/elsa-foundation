using Elsa.Caching.Memory.Options;
using Elsa.Caching.Memory.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Caching.Tests;

public sealed class CacheManagerTests : IDisposable
{
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly CacheManager _cacheManager;

    public CacheManagerTests()
    {
        var invoker = new ChangeTokenSignalInvoker();
        var signaler = new ChangeTokenSignaler(invoker);
        var options = Options.Create(new CachingOptions { CacheDuration = TimeSpan.FromMinutes(1) });
        _cacheManager = new CacheManager(_memoryCache, signaler, options);
    }

    [Fact]
    public async Task FindOrCreateAsync_Round_Trips_A_Value_And_Only_Invokes_The_Factory_Once()
    {
        var callCount = 0;

        Task<string> Factory(ICacheEntry entry)
        {
            callCount++;
            return Task.FromResult("value");
        }

        var first = await _cacheManager.FindOrCreateAsync<string>("my-key", Factory);
        var second = await _cacheManager.FindOrCreateAsync<string>("my-key", Factory);

        Assert.Equal("value", first);
        Assert.Equal("value", second);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task FindOrCreateAsync_Reinvokes_The_Factory_After_Its_Change_Token_Is_Triggered()
    {
        var callCount = 0;

        Task<string> Factory(ICacheEntry entry)
        {
            callCount++;
            entry.ExpirationTokens.Add(_cacheManager.GetToken("invalidation-key"));
            return Task.FromResult($"value-{callCount}");
        }

        var first = await _cacheManager.FindOrCreateAsync<string>("my-key", Factory);
        Assert.Equal("value-1", first);
        Assert.Equal(1, callCount);

        // Still cached: factory must not run again.
        var cached = await _cacheManager.FindOrCreateAsync<string>("my-key", Factory);
        Assert.Equal("value-1", cached);
        Assert.Equal(1, callCount);

        await _cacheManager.TriggerTokenAsync("invalidation-key");

        // The change token fired, so the entry must be treated as expired and recomputed.
        var afterInvalidation = await _cacheManager.FindOrCreateAsync<string>("my-key", Factory);
        Assert.Equal("value-2", afterInvalidation);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetToken_Returns_A_Token_That_Is_Not_Yet_Changed()
    {
        var token = _cacheManager.GetToken("some-key");

        Assert.False(token.HasChanged);
    }

    public void Dispose() => _memoryCache.Dispose();
}
