using Elsa.Caching.Core;
using Elsa.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Caching.Tests;

public sealed class MemoryCacheFeatureRegistrationTests
{
    [Fact]
    public void Memory_cache_feature_registers_cache_manager()
    {
        var services = new ServiceCollection();
        var feature = new MemoryCacheFeature();

        feature.ConfigureServices(services);

        // MD-10 (§2.23.1): assert the cache-manager contract is registered, not its implementation type or lifetime.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ICacheManager));
    }
}
