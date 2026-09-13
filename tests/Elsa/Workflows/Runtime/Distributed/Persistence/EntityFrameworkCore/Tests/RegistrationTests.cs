using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var originalTransport = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore) && descriptor.ImplementationFactory is not null);
        Assert.Same(originalTransport, services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport)));
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
    public void Conflicting_reregistration_fails_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=different.db"
        }));
        Assert.Equal(before, services);
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
    public void Distributed_default_preserves_an_explicit_unmarked_store_and_EF_rejects_it()
    {
        var services = new ServiceCollection();
        services.AddScoped<IExecutionPlacementStore, ExplicitStore>();
        var explicitDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);

        Assert.Same(explicitDescriptor, services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore)));
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationInstance is ExecutionPlacementStoreBackend);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options));
        Assert.Equal(before, services);
    }

    [Fact]
    public void EF_rejects_a_store_replaced_after_the_backend_claimed_ownership()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Scoped<IExecutionPlacementStore, ExplicitStore>());
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options));
        Assert.Equal(before, services);
    }

    [Fact]
    public void Groundwork_rejects_an_unmarked_store_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IExecutionPlacementStore, ExplicitStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkDistributedRuntimeStores());
        Assert.Equal(before, services);
    }

    [Fact]
    public void EF_snapshots_mutable_registration_options()
    {
        var services = new ServiceCollection();
        var supplied = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=original.db",
            ConnectionName = "Original"
        };

        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(supplied);
        supplied.Provider = "SqlServer";
        supplied.ConnectionString = "Server=changed";
        supplied.ConnectionName = "Changed";

        var configured = Assert.IsType<DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions>(
            services.Single(descriptor => descriptor.ServiceType == typeof(DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions)).ImplementationInstance);
        Assert.NotSame(supplied, configured);
        Assert.Equal("Sqlite", configured.Provider);
        Assert.Equal("Data Source=original.db", configured.ConnectionString);
        Assert.Equal("Original", configured.ConnectionName);
    }

    [Fact]
    public void Groundwork_then_EF_replaces_only_the_placement_store()
    {
        var services = new ServiceCollection();
        services.AddGroundworkDistributedRuntimeStores();
        var originalTransport = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));

        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        Assert.Equal(ExecutionPlacementStoreBackend.EntityFramework, Backend(services));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Same(originalTransport, services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport)));
    }

    [Fact]
    public void EF_then_Groundwork_preserves_EF_placement_and_replaces_transport()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        services.AddGroundworkDistributedRuntimeStores();
        var groundworkTransport = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        Assert.Equal(ExecutionPlacementStoreBackend.EntityFramework, Backend(services));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Same(groundworkTransport, services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport)));
    }

    [Fact]
    public async Task Registration_builds_and_resolves_the_provider_context_accessor_and_scoped_store()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-placement-registration-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddPersistenceCore("registration-scope");
            services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={databasePath}"
            });

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ExecutionPlacementDbContext>();
            var accessor = scope.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextAccessor>();
            var store = scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>();

            Assert.IsType<ExecutionPlacementSqliteDbContext>(context);
            Assert.NotNull(accessor.Current.Scope);
            Assert.IsType<EfExecutionPlacementStore>(store);
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var claimed = await store.TryClaimAsync(new("wf-registration", "node-registration", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1)), DateTimeOffset.UtcNow);
            Assert.Equal(ExecutionPlacementClaimOutcome.Granted, claimed.Outcome);
            Assert.NotNull(await store.FindAsync("wf-registration"));
        }
        finally
        {
            foreach (var file in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
                File.Delete(file);
        }
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
