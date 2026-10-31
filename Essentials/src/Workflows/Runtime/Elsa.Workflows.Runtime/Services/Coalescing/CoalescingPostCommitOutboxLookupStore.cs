using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// Coalescing-aware overlay for <see cref="IPostCommitOutboxLookupStore"/>. An item the active session holds is found in
/// its working set; any other item comes from the lookup the host registered, which this wraps rather than replaces.
/// </summary>
internal sealed class CoalescingPostCommitOutboxLookupStore(
    CoalescingInner<IPostCommitOutboxLookupStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IPostCommitOutboxLookupStore
{
    public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
        FindAsync(sessionAccessor, inner.Value, outboxItemId, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
        inner.Value.FindDurableAsync(outboxItemId, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxItem?> FindUndeliveredDurableAsync(string workflowExecutionId, string excludedIntentKind,
        CancellationToken cancellationToken = default) =>
        inner.Value.FindUndeliveredDurableAsync(workflowExecutionId, excludedIntentKind, cancellationToken);

    /// <summary>The session's item when it holds one, otherwise <paramref name="lookup"/>'s answer.</summary>
    internal static ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
        IRuntimeCoalescingSessionAccessor sessionAccessor,
        IPostCommitOutboxLookupStore? lookup,
        string outboxItemId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        cancellationToken.ThrowIfCancellationRequested();

        if (sessionAccessor.Current is { IsActive: true } session && session.TryFindOutboxItem(outboxItemId, out var overlayItem))
            return new ValueTask<RuntimePostCommitOutboxItem?>(overlayItem);

        return (lookup ?? throw new InvalidOperationException(
                "The configured post-commit outbox store does not provide exact-item lookup support."))
            .FindAsync(outboxItemId, cancellationToken);
    }
}
