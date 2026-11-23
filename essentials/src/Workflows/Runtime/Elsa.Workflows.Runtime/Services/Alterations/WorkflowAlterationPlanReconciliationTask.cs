using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Derives plan terminal counts and cooperative cancellation completion from authoritative job records.</summary>
public sealed class WorkflowAlterationPlanReconciliationTask(
    IWorkflowAlterationStore store,
    TimeProvider? timeProvider = null)
{
    private readonly IWorkflowAlterationStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask<WorkflowAlterationPlanState> ReconcileAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        return _store.ReconcileAsync(planId, _timeProvider.GetUtcNow(), cancellationToken);
    }
}
