using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;

namespace Elsa.Workflows.Runtime.Tests;

// Exercises the compatibility path for queue providers without visibility snapshots.
internal class QueueWithoutVisibility(InMemoryWorkflowSchedulerWorkQueue inner)
    : IWorkflowSchedulerWorkQueue, IWorkflowSchedulerWorkClaimInspection
{
    public bool SupportsClaimTransitions => true;
    public bool SupportsTargetedDeletion => true;
    public bool SupportsClaimableBacklogDiscovery => true;
    public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem item, CancellationToken token = default) => inner.EnqueueAsync(item, token);
    public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken token = default) => inner.ListAsync(query, token);
    public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string id, CancellationToken token = default) => inner.DequeueAsync(id, token);
    public ValueTask<bool> DeleteAsync(string id, string workId, CancellationToken token = default) => inner.DeleteAsync(id, workId, token);
    public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken token = default) => inner.ListPendingWorkflowExecutionIdsAsync(limit, token);
    public ValueTask<IReadOnlyCollection<string>> ListClaimableWorkflowExecutionIdsAsync(RuntimeSchedulerClaimableBacklogQuery query, CancellationToken token = default) => inner.ListClaimableWorkflowExecutionIdsAsync(query, token);
    public virtual ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest request, CancellationToken token = default) => inner.ClaimAsync(request, token);
    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset now, TimeSpan timeout, CancellationToken token = default) => inner.RenewClaimAsync(claim, now, timeout, token);
    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(RuntimeSchedulerWorkClaim claim, CancellationToken token = default) => inner.CompleteClaimAsync(claim, token);
    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset visibleAt, CancellationToken token = default) => inner.ReleaseClaimAsync(claim, visibleAt, token);
    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(ConsumedSchedulerWorkItem consumed, CancellationToken token = default) => inner.ConsumeClaimedAsync(consumed, token);
    public ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkClaim>> ListActiveClaimsAsync(string id, DateTimeOffset now, CancellationToken token = default) => inner.ListActiveClaimsAsync(id, now, token);
}

