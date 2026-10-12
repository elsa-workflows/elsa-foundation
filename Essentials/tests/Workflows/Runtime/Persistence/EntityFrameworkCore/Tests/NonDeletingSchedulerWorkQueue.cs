using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// A scheduler queue that cannot delete a work item by its identity, as a provider that does not opt in to targeted
/// deletion. A drain over it cannot fold, so it commits every checkpoint as it is made and delivers its own continuations
/// through the outbox. Everything else is forwarded to the queue it wraps.
/// </summary>
internal sealed class NonDeletingSchedulerWorkQueue(IWorkflowSchedulerWorkQueue inner) : IWorkflowSchedulerWorkQueue
{
    /// <summary>Wraps the effective <see cref="IWorkflowSchedulerWorkQueue"/> registration, keeping its lifetime.</summary>
    public static void Decorate(IServiceCollection services)
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(IWorkflowSchedulerWorkQueue));
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(IWorkflowSchedulerWorkQueue),
            provider => new NonDeletingSchedulerWorkQueue((IWorkflowSchedulerWorkQueue)(
                descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))),
            descriptor.Lifetime));
    }

    public bool SupportsTargetedDeletion => false;
    public bool SupportsClaimTransitions => inner.SupportsClaimTransitions;
    public bool SupportsClaimableBacklogDiscovery => inner.SupportsClaimableBacklogDiscovery;

    public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
        inner.EnqueueAsync(workItem, cancellationToken);

    public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
        inner.ListAsync(query, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        inner.DequeueAsync(workflowExecutionId, cancellationToken);

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
        inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> ListClaimableWorkflowExecutionIdsAsync(
        RuntimeSchedulerClaimableBacklogQuery query,
        CancellationToken cancellationToken = default) =>
        inner.ListClaimableWorkflowExecutionIdsAsync(query, cancellationToken);

    public ValueTask<IReadOnlyDictionary<string, RuntimeSchedulerWorkItem>> ListNextWorkItemsAsync(
        IReadOnlyCollection<string> workflowExecutionIds,
        CancellationToken cancellationToken = default) =>
        inner.ListNextWorkItemsAsync(workflowExecutionIds, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken = default) =>
        inner.ClaimAsync(request, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default) =>
        inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken = default) =>
        inner.CompleteClaimAsync(claim, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default) =>
        inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(
        ConsumedSchedulerWorkItem consumed,
        CancellationToken cancellationToken = default) =>
        inner.ConsumeClaimedAsync(consumed, cancellationToken);
}
