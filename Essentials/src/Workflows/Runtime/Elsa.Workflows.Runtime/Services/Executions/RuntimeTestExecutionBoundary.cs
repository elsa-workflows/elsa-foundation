using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Executions;

internal sealed class RuntimeTestExecutionBoundary(IServiceProvider services)
{
    internal async ValueTask RequireAsync(WorkflowTestEntry entry, CancellationToken cancellationToken)
    {
        var routing = services.GetServices<IRuntimeTestExecutionRoutingBoundary>().ToArray();
        if (routing.Length != 1)
            throw WorkflowTestEntryAdmission.Refused("test-entry-provider-unavailable", "Selected test execution requires exactly one physical persistence boundary and routing boundary.");
        if (!CanPersist(entry) || !await routing[0].CanRouteTestEntryAsync(entry,
                services.GetRequiredService<IWorkflowExecutionActorProvider>(), cancellationToken))
            throw WorkflowTestEntryAdmission.Refused("test-entry-provider-unavailable", "The selected physical providers cannot fence predecessor consumers of test execution.");
    }

    internal bool CanPersist(WorkflowTestEntry entry)
    {
        var persistence = services.GetServices<IRuntimeTestPersistenceBoundary>().ToArray();
        if (persistence.Length != 1) return false;
        var executions = Physical<IWorkflowExecutionStateStore>(typeof(CoalescingWorkflowExecutionStateStore));
        var scheduler = Physical<IWorkflowSchedulerWorkQueue>(typeof(CoalescingWorkflowSchedulerWorkQueue));
        var checkpoints = Physical<IRuntimeCheckpointCommitStore>(typeof(CoalescingRuntimeCheckpointCommitStore));
        var outbox = Physical<IRuntimePostCommitOutboxStore>(typeof(CoalescingRuntimePostCommitOutboxStore));
        var context = new RuntimeTestPersistenceBoundaryContext(entry, executions, scheduler, checkpoints, outbox,
            services.GetRequiredService<IBackgroundActivityWorkStore>(), services.GetRequiredService<IDurableTimerStore>());
        return persistence[0].CanPersistTestEntry(context);
    }

    private T Physical<T>(Type knownDecorator) where T : class
    {
        var selected = services.GetRequiredService<T>();
        return selected.GetType() == knownDecorator
            ? services.GetRequiredService<CoalescingInner<T>>().Value : selected;
    }
}

/// <summary>Only these actual process-local stores can share this process's selected test interpreter.</summary>
internal sealed class InMemoryRuntimeTestPersistenceBoundary(IServiceProvider services) : IRuntimeTestPersistenceBoundary
{
    public bool CanPersistTestEntry(RuntimeTestPersistenceBoundaryContext context)
    {
        try
        {
            var selectedQueue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var physicalQueue = selectedQueue.GetType() == typeof(CoalescingWorkflowSchedulerWorkQueue)
                ? services.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value : selectedQueue;
            return context.Executions is InMemoryWorkflowExecutionStateStore executions &&
                context.Checkpoints is InMemoryRuntimeCheckpointCommitStore checkpoints &&
                ReferenceEquals(context.Outbox, checkpoints) && ReferenceEquals(executions.CheckpointState, checkpoints.State) && checkpoints.CoordinatesTestExecutions(executions) &&
                context.Scheduler is InMemoryWorkflowSchedulerWorkQueue && ReferenceEquals(context.Scheduler, physicalQueue) &&
                context.Timers is InMemoryDurableTimerStore && ReferenceEquals(context.Timers, services.GetRequiredService<IDurableTimerStore>()) &&
                context.Background is InMemoryBackgroundActivityWorkStore background && background.Coordinates(checkpoints.State, context.Entry.Scope.Partition.Value);
        }
        catch (InvalidOperationException) { return false; }
    }
}

internal sealed class InProcessRuntimeTestExecutionRoutingBoundary(IServiceProvider services) : IRuntimeTestExecutionRoutingBoundary
{
    public async ValueTask<bool> CanRouteTestEntryAsync(WorkflowTestEntry entry, IWorkflowExecutionActorProvider actors,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (actors is not InProcessWorkflowExecutionActorProvider local) return false;
        var scopeFactory = services.GetRequiredService<IPersistenceOperationScopeFactory>();
        if (!local.SupportsCurrentTestEntryExecutor(services.GetRequiredService<IWorkflowExecutionCommandExecutor>(), scopeFactory)) return false;
        await using var operation = await scopeFactory.CreateAsync(new PersistenceScope(entry.Scope.Partition.Value), cancellationToken);
        return operation.ServiceProvider.GetService<RuntimeTestExecutionBoundary>()?.CanPersist(entry) == true;
    }
}
