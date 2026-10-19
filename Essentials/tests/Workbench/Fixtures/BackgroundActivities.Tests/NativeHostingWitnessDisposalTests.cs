using Elsa.Workbench.Tests.BackgroundActivities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench.Tests.BackgroundActivities.Tests;

public class NativeHostingWitnessDisposalTests
{
    [Fact]
    public void Sync_scope_disposes_factory_created_inner_services_once_and_keeps_scoped_identity()
    {
        var counts = new NativeHostingWitnessDisposalCounts();
        var (services, _) = NativeHostingWitnessTestServices.Create(counts);
        NativeHostingWitnessTestServices.Register(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        IRuntimeExecutionOwnershipService firstOwnership;
        IExecutionPlacementStore firstPlacement;
        IExecutionCommandTransport firstTransport;
        using (var firstScope = provider.CreateScope())
        {
            firstOwnership = firstScope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
            firstPlacement = firstScope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>();
            firstTransport = firstScope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>();
            Assert.IsType<NativeHostingOwnershipWitness>(firstOwnership);
            Assert.IsType<NativeHostingPlacementStoreWitness>(firstPlacement);
            Assert.IsType<NativeHostingCommandTransportWitness>(firstTransport);
            Assert.Same(firstOwnership, firstScope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>());
            Assert.Same(firstPlacement, firstScope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>());
            Assert.Same(firstTransport, firstScope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>());
        }

        Assert.Equal(1, counts.Ownership);
        Assert.Equal(1, counts.Placement);
        Assert.Equal(1, counts.Transport);

        using (var secondScope = provider.CreateScope())
        {
            Assert.NotSame(firstOwnership, secondScope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>());
            Assert.NotSame(firstPlacement, secondScope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>());
            Assert.NotSame(firstTransport, secondScope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>());
        }

        Assert.Equal(2, counts.Ownership);
        Assert.Equal(2, counts.Placement);
        Assert.Equal(2, counts.Transport);
    }

    [Fact]
    public async Task Async_scope_disposes_factory_created_inner_services_once()
    {
        var counts = new NativeHostingWitnessDisposalCounts();
        var (services, _) = NativeHostingWitnessTestServices.Create(counts);
        NativeHostingWitnessTestServices.Register(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
            _ = scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>();
            _ = scope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>();
        }

        Assert.Equal(1, counts.Ownership);
        Assert.Equal(1, counts.Placement);
        Assert.Equal(1, counts.Transport);
    }
}
