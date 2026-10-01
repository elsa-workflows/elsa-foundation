using CShells.Lifecycle;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Whole runtime nodes over one database for the keyed-start scenarios (#2195, #2198). Every node is a fresh service
/// provider, so it shares nothing with the other nodes but the database.
/// </summary>
internal static class KeyedStartNodes
{
    private const string RecoverySigningKey = "ef-runtime-publish-stimulus-recovery-signing-key-32";
    private const string HierarchySigningKey = "ef-runtime-publish-stimulus-hierarchy-signing-key-32";

    public static async Task<WorkflowExecutionHarness> StartAsync(
        string provider,
        string connectionString,
        Action<IServiceCollection>? configure = null)
    {
        var node = WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services
                    .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                    {
                        Provider = provider,
                        ConnectionString = connectionString,
                        RecoveryContinuationSigningKey = RecoverySigningKey,
                        HierarchyCursorSigningKey = HierarchySigningKey
                    })
                    .AddEfModuleMigrations<RuntimeDbContext>(provider);
                new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
                // Real ids: the harness default names every execution alike, which would hide a second start.
                services.Replace(ServiceDescriptor.Singleton<IRuntimeExecutionIdGenerator>(
                    _ => new ShortRuntimeExecutionIdGenerator(TimeProvider.System)));
                configure?.Invoke(services);
            })
            .Build();

        foreach (var initializer in node.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        node.InitializeActivityTypes();
        return node;
    }

    /// <summary>
    /// The node holds exactly one execution, the keyed one, still running, with the one activity execution its start ran,
    /// and nothing poisoned or faulted: a start that ran twice would have collided with it.
    /// </summary>
    public static async Task AssertStartedOnceAsync(WorkflowExecutionHarness node, string keyedWorkflowExecutionId)
    {
        await using var scope = node.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var execution = Assert.Single(await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync());
        Assert.Equal(keyedWorkflowExecutionId, execution.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Running, execution.Status);
        Assert.Single(await services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(execution.WorkflowExecutionId));
        Assert.Empty(await services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(execution.WorkflowExecutionId));
        Assert.Empty(await services.GetRequiredService<IIncidentStateStore>().ListAsync(execution.WorkflowExecutionId));
    }

    /// <summary>Routes through the node's real router in a fresh scope, as a pump's or the outbox's handler scope does.</summary>
    public sealed class ScopedStimulusRouter(WorkflowExecutionHarness node) : IStimulusRouter
    {
        public StimulusRoutingResult? LastResult { get; private set; }

        public async ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            await using var scope = node.Services.CreateAsyncScope();
            return LastResult = await scope.ServiceProvider.GetRequiredService<IStimulusRouter>().RouteAsync(request, cancellationToken);
        }
    }
}
