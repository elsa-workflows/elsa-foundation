using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the persistence feature swaps the two leaf store contracts to the Groundwork bridges regardless of
/// feature composition order (the distributed feature registers its in-memory defaults with TryAdd; the bridge
/// registration uses RemoveAll + Add).
/// </summary>
public sealed class GroundworkDistributedRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Feature_SwapsBothStoreContractsToTheGroundworkBridges(bool distributedFeatureFirst)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create()));

        var distributedFeature = new WorkflowsRuntimeDistributedFeature();
        var persistenceFeature = new WorkflowsRuntimeDistributedGroundworkPersistenceFeature();

        if (distributedFeatureFirst)
        {
            distributedFeature.ConfigureServices(services);
            persistenceFeature.ConfigureServices(services);
        }
        else
        {
            persistenceFeature.ConfigureServices(services);
            distributedFeature.ConfigureServices(services);
        }

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GroundworkExecutionPlacementStore>(provider.GetRequiredService<IExecutionPlacementStore>());
        Assert.IsType<GroundworkExecutionCommandTransport>(provider.GetRequiredService<IExecutionCommandTransport>());
    }
}
