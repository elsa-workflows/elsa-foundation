using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Claims a bounded number of sealed-plan jobs and routes each through its target workflow actor.</summary>
public sealed class WorkflowAlterationJobTask
{
    private readonly IWorkflowAlterationStore _store;
    private readonly IWorkflowAlterationJobDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public WorkflowAlterationJobTask(
        IWorkflowAlterationStore store,
        IWorkflowAlterationJobDispatcher dispatcher,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _store = store;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<IReadOnlyList<WorkflowAlterationActorDispatchResult>> DispatchAvailableAsync(
        string planId,
        string workerId,
        int maximumClaims,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (maximumClaims <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumClaims));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var results = new List<WorkflowAlterationActorDispatchResult>(maximumClaims);
        for (var count = 0; count < maximumClaims; count++)
        {
            var job = await _store.ClaimNextAsync(planId, workerId, _timeProvider.GetUtcNow(), leaseDuration, cancellationToken);
            if (job is null)
                break;
            results.Add(await _dispatcher.DispatchAsync(job, cancellationToken));
        }
        return results;
    }
}
