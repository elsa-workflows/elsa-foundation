using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemorySchedulerStateStore() : InMemoryKeyedStateStore<string, SchedulerState>(StringComparer.Ordinal), ISchedulerStateStore
{
    public override bool IsAffected(InMemoryCheckpointMutationPlan plan) => plan.MutatesScheduler;

    private protected override bool IsCheckpointKey(string key, InMemoryCheckpointMutationPlan scope) =>
        scope.MutatesScheduler && StringComparer.Ordinal.Equals(key, scope.WorkflowExecutionId);

    private protected override IEnumerable<string> CheckpointKeys(InMemoryCheckpointMutationPlan scope) =>
        scope.MutatesScheduler ? [scope.WorkflowExecutionId] : [];

    public ValueTask<SchedulerState> SaveAsync(SchedulerState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Save(state.WorkflowExecutionId, state));
    }

    public ValueTask<SchedulerState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Find(workflowExecutionId));
    }

    public ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new(SnapshotAll());
    }
}
