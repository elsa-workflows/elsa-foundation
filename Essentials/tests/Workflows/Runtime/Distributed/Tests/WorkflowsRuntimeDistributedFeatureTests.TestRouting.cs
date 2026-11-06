using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Cluster.Core.Options;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

public sealed partial class WorkflowsRuntimeDistributedFeatureTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public async Task Selected_routing_qualifies_the_actual_partition_transport_before_any_claim_or_mailbox(int owners, bool customExecutor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowRuntime();
        services.Configure<ClusterMembershipOptions>(options => options.HostId = "node-a");
        services.AddSingleton(new JoinSweepLedger());
        if (customExecutor) services.AddSingleton(new InProcessWorkflowExecutionActorProvider(NoopWorkflowExecutionCommandExecutor.Instance));
        services.AddSingleton<IWorkflowExecutionLeaseFencingCapability, AvailableFencing>();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);
        var witness = new RoutingWitness();
        for (var i = 0; i < owners; i++)
            services.AddScoped<IExecutionCommandTransportTestBoundary>(provider => new RoutingOwner(
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(), provider.GetRequiredService<IExecutionCommandTransport>(), witness));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var entry = new WorkflowTestEntry("context", "fingerprint", new("artifact", "definition", "version", "1.0.0", "sha256:exact"),
            "selected", new("scope", DateTimeOffset.UtcNow.AddMinutes(10), null, new("partition")), new("tests", "tests"));
        var route = scope.ServiceProvider.GetRequiredService<IRuntimeTestExecutionRoutingBoundary>();
        var actual = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        Assert.Equal(owners == 1 && !customExecutor, await route.CanRouteTestEntryAsync(entry, actual));
        Assert.Equal(owners == 1 && !customExecutor ? "partition" : null, witness.Scope);
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("partition")));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>().FindAsync("execution"));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>().CountPendingAsync("execution"));
        if (owners > 1)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
            });
            Assert.Contains("qualification owner", error.Message, StringComparison.Ordinal);
        }
    }

    private sealed class AvailableFencing : IWorkflowExecutionLeaseFencingCapability
    { public bool IsAvailable => true; }
    private sealed class RoutingWitness { public string? Scope { get; set; } }
    private sealed class RoutingOwner(IPersistenceAccessContextAccessor access, IExecutionCommandTransport actual, RoutingWitness witness)
        : IExecutionCommandTransportTestBoundary
    {
        public bool CanTransportTestEntry(WorkflowTestEntry entry, IExecutionCommandTransport physicalTransport)
        {
            witness.Scope = access.Current.RequireScope().Value;
            return ReferenceEquals(actual, physicalTransport) && witness.Scope == entry.Scope.Partition.Value;
        }
    }
}
