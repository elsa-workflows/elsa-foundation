using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
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
        var executions = Physical<IWorkflowExecutionStateStore>();
        var scheduler = Physical<IWorkflowSchedulerWorkQueue>();
        var checkpoints = Physical<IRuntimeCheckpointCommitStore>();
        var outbox = Physical<IRuntimePostCommitOutboxStore>();
        var context = new RuntimeTestPersistenceBoundaryContext(entry, executions, scheduler, checkpoints, outbox,
            services.GetRequiredService<IBackgroundActivityWorkStore>(), services.GetRequiredService<IDurableTimerStore>());
        return persistence[0].CanPersistTestEntry(context);
    }

    private T Physical<T>() where T : class
    {
        var selected = services.GetRequiredService<T>();
        return selected switch
        {
            CoalescingWorkflowExecutionStateStore wrapper => (T)wrapper.PhysicalStore,
            CoalescingWorkflowSchedulerWorkQueue wrapper => (T)wrapper.PhysicalStore,
            CoalescingRuntimeCheckpointCommitStore wrapper => (T)wrapper.PhysicalStore,
            CoalescingRuntimePostCommitOutboxStore wrapper => (T)wrapper.PhysicalStore,
            _ => selected
        };
    }
}
