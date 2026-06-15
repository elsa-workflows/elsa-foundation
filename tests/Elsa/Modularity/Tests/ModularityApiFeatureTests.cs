using Elsa.Modularity.Api;
using Elsa.Modularity.Core.Contracts;
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
        Assert.Contains(services, x => x.ServiceType == typeof(IRuntimeFeatureCatalogRefresher));
    }
}
