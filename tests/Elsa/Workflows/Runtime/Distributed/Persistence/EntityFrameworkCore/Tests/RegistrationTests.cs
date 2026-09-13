using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests;

public sealed class RegistrationTests
{
    private static readonly DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions Options = new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=registration-test.db"
    };

    [Fact]
    public void EF_replaces_only_placement_after_the_distributed_default_is_composed()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore) && descriptor.ImplementationFactory is not null);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
        Assert.Equal(
            ExecutionPlacementStoreBackend.EntityFramework,
            services.Single(descriptor => descriptor.ImplementationInstance is ExecutionPlacementStoreBackend).ImplementationInstance is ExecutionPlacementStoreBackend backend ? backend.Name : null);
    }

    [Fact]
    public void Repeating_the_same_registration_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);
        var before = services.Count;
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
        {
            Provider = Options.Provider,
            ConnectionString = Options.ConnectionString
        });
        Assert.Equal(before, services.Count);
    }

    [Fact]
    public void Explicit_unmarked_store_is_rejected_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IExecutionPlacementStore, ExplicitStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options));
        Assert.Equal(before, services);
    }

    [Fact]
    public void Groundwork_then_EF_replaces_only_the_placement_store()
    {
        var services = new ServiceCollection();
        services.AddGroundworkDistributedRuntimeStores();

        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        Assert.Equal(ExecutionPlacementStoreBackend.EntityFramework, Backend(services));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
    }

    [Fact]
    public void EF_then_Groundwork_preserves_EF_placement_and_replaces_transport()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        services.AddGroundworkDistributedRuntimeStores();

        Assert.Equal(ExecutionPlacementStoreBackend.EntityFramework, Backend(services));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
    }

    private static string Backend(IServiceCollection services) => services
        .Single(descriptor => descriptor.ImplementationInstance is ExecutionPlacementStoreBackend)
        .ImplementationInstance is ExecutionPlacementStoreBackend backend
        ? backend.Name
        : throw new InvalidOperationException("The placement backend marker was not registered.");

    private sealed class ExplicitStore : IExecutionPlacementStore
    {
        public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(ExecutionPlacementLeaseListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
