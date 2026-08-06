using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Captures every scheduler work item enqueued during a run so tests can assert on derived work-item metadata.</summary>
public sealed class EnqueuedWorkItemRecorder
{
    private readonly List<RuntimeSchedulerWorkItem> _items = [];

    public IReadOnlyList<RuntimeSchedulerWorkItem> Items => _items;

    public void Record(RuntimeSchedulerWorkItem item) => _items.Add(item);
}

/// <summary>
/// Transparent decorator over the registered <see cref="IWorkflowSchedulerWorkQueue"/> that records every
/// enqueued work item into an <see cref="EnqueuedWorkItemRecorder"/> before delegating. Used by the #989
/// metadata-hygiene pin to inspect the <c>CommandMetadata</c> of work items derived from a fault evaluation.
/// </summary>
internal sealed class RecordingSchedulerWorkQueue(IWorkflowSchedulerWorkQueue inner, EnqueuedWorkItemRecorder recorder) : IWorkflowSchedulerWorkQueue, IInMemoryCheckpointTransactionSource
{
    public bool SupportsClaimTransitions => inner.SupportsClaimTransitions;

    IEnumerable<object?> IInMemoryCheckpointTransactionSource.GetCheckpointTransactionParticipants() => [inner];

    public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        recorder.Record(workItem);
        return inner.EnqueueAsync(workItem, cancellationToken);
    }

    public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
        inner.ListAsync(query, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        inner.DequeueAsync(workflowExecutionId, cancellationToken);

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
        inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest request, CancellationToken cancellationToken = default) =>
        inner.ClaimAsync(request, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) =>
        inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(RuntimeSchedulerWorkClaim claim, CancellationToken cancellationToken = default) =>
        inner.CompleteClaimAsync(claim, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default) =>
        inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(ConsumedSchedulerWorkItem consumed, CancellationToken cancellationToken = default) =>
        inner.ConsumeClaimedAsync(consumed, cancellationToken);

    /// <summary>Registers the recorder and wraps the currently-registered work queue with a recording decorator.</summary>
    public static void Register(IServiceCollection services, EnqueuedWorkItemRecorder recorder)
    {
        services.AddSingleton(recorder);

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IWorkflowSchedulerWorkQueue))
            ?? throw new InvalidOperationException("No IWorkflowSchedulerWorkQueue is registered to decorate.");
        services.Remove(descriptor);

        services.AddSingleton<IWorkflowSchedulerWorkQueue>(sp =>
        {
            var queueRecorder = sp.GetRequiredService<EnqueuedWorkItemRecorder>();
            var innerQueue = (IWorkflowSchedulerWorkQueue)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
            return new RecordingSchedulerWorkQueue(innerQueue, queueRecorder);
        });
    }
}
