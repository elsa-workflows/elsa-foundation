using Elsa.Modularity.Api;
using Elsa.Modularity.Api.Services;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ModularityApiFeatureTests
{
    [Fact]
    public void RegistersFeatureManagementServices()
    {
        var services = new ServiceCollection();

        new ModularityApiFeature().ConfigureServices(services);

        Assert.Contains(services, x => x.ServiceType == typeof(IFeatureManagementService));
        Assert.Contains(services, x => x.ServiceType == typeof(IShellFeatureConfigurationStore));
        Assert.Contains(services, x => x.ServiceType == typeof(IShellReloader));
        Assert.Contains(services, x => x.ServiceType == typeof(IRuntimeFeatureCatalogAccessor));
        Assert.Contains(services, x => x.ServiceType == typeof(IRuntimeFeatureCatalogRefresher));
    }

    [Fact]
    public void ReplacesHostLevelShellConfigurationFallbacks()
    {
        var services = new ServiceCollection();
        services.AddScoped<IShellFeatureConfigurationStore, NullShellFeatureConfigurationStore>();
        services.AddScoped<IShellReloader, NullShellReloader>();

        new ModularityApiFeature().ConfigureServices(services);

        Assert.Equal(typeof(JsonShellFeatureConfigurationStore), SingleImplementation<IShellFeatureConfigurationStore>(services));
        Assert.Equal(typeof(ShellReloader), SingleImplementation<IShellReloader>(services));
    }

    private static Type SingleImplementation<TService>(IServiceCollection services) =>
        Assert.Single(services.Where(x => x.ServiceType == typeof(TService))).ImplementationType!;

    private sealed class NullShellFeatureConfigurationStore : IShellFeatureConfigurationStore
    {
        public Task<ShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ShellFeatureConfigurationSnapshot> SaveAsync(
            string expectedRevision,
            IReadOnlyList<FeatureConfigurationChange> features,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NullShellReloader : IShellReloader
    {
        public Task<int> ReloadAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
