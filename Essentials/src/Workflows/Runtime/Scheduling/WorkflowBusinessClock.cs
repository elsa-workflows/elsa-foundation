using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Scheduling;

internal sealed class WorkflowBusinessClock(
    WorkflowTestTimePolicyResolver policies,
    IControlledWorkflowTimeStore timeStore,
    TimeProvider wall) : IWorkflowBusinessClock
{
    public async ValueTask<WorkflowBusinessTime> ReadAsync(ActivityExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var policy = await policies.ResolveAsync(context.WorkflowExecutionId, context.InvocationId,
            context.ExecutableNodeId, cancellationToken);
        if (policy is null || policy.Mode == WorkflowTestTimeMode.Wall)
            return new WorkflowBusinessTime(wall.GetUtcNow());
        var clock = await timeStore.FindClockAsync(policy, cancellationToken);
        if (clock is null || clock.Closed || !policy.Matches(clock.Policy) || clock.Revision <= 0)
            throw WorkflowTestTimePolicyResolver.Refuse("test-clock-unavailable", "The admitted controlled clock is unavailable.");
        return clock.Read();
    }
}
