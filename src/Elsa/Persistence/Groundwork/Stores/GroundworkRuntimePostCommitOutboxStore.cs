using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IRuntimePostCommitOutboxStore"/> for the Groundwork bridge, backed by the portable
/// <see cref="IDocumentStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// This bridge deliberately uses the portable document store rather than Groundwork's operational
/// <c>IOutboxStore</c>. The operational outbox is a lease/claim message queue: it generates its own message
/// identity and hands out lease tokens that must be presented to acknowledge delivery. Elsa's post-commit
/// outbox contract is different — the caller supplies a deterministic <see cref="RuntimePostCommitOutboxItem.OutboxItemId"/>,
/// records delivery results by that id with no lease token, and the checkpoint committer's inline dispatch path
/// records a <c>Delivered</c> result without ever acquiring a lease. Modelling each outbox item as a document
/// keeps the runtime persistence story on a single portable substrate and reproduces the authoritative
/// in-memory lifecycle exactly, now durable.
/// </para>
/// </remarks>
public sealed class GroundworkRuntimePostCommitOutboxStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer) : IRuntimePostCommitOutboxStore
{
    public async ValueTask SavePendingAsync(RuntimePostCommitOutboxItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");

        var existing = await LoadAsync(item.OutboxItemId, cancellationToken);
        if (existing is not null)
        {
            if (IsSamePendingIntent(existing, item))
                return;
            throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
        }

        await SaveAsync(item, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.OwnerId is not null)
            throw new NotSupportedException("The Groundwork post-commit outbox store does not implement delivery ownership filtering.");

        var envelopes = query.WorkflowExecutionId is { } workflowExecutionId
            ? await store.QueryAsync(
                new DocumentStoreQuery(
                    ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                    ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                    workflowExecutionId),
                cancellationToken)
            : await store.QueryAsync(
                new DocumentStoreQuery(
                    ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                    ElsaRuntimeStorageManifest.ByCollectionIndex,
                    ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind),
                cancellationToken);

        return envelopes
            .Select(Map)
            .Where(item => IsDeliverable(item, query))
            .OrderBy(item => item.AvailableAt ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.RecordedAt)
            .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal)
            .Take(query.Limit)
            .ToArray();
    }

    public async ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await LoadAsync(result.OutboxItemId, cancellationToken);
        if (existing is null)
            throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' was not found.");
        if (existing.IsTerminal)
            throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is already terminal.");

        var deliveryAttemptCount = existing.DeliveryAttemptCount + 1;
        var status = NormalizeDeliveryStatus(existing, result.Status, deliveryAttemptCount);
        DateTimeOffset? availableAt = status == RuntimePostCommitOutboxStatus.FailedRetryable
            ? NextRetryAvailableAt(existing, result.RecordedAt)
            : null;

        var updated = new RuntimePostCommitOutboxItem(
            outboxItemId: existing.OutboxItemId,
            intent: existing.Intent,
            status: status,
            recordedAt: existing.RecordedAt,
            availableAt: availableAt,
            retryPolicy: existing.RetryPolicy,
            deliveryAttemptCount: deliveryAttemptCount,
            deliveringOwnerId: null,
            deliveryStartedAt: null,
            deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
            lastFailureMessage: result.FailureMessage,
            metadata: existing.Metadata);

        await SaveAsync(updated, cancellationToken);
    }

    private async ValueTask<RuntimePostCommitOutboxItem?> LoadAsync(string outboxItemId, CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            outboxItemId,
            cancellationToken);
        return envelope is null ? null : Map(envelope);
    }

    private async ValueTask SaveAsync(RuntimePostCommitOutboxItem item, CancellationToken cancellationToken)
    {
        var envelope = new OutboxEnvelope(
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            item.Intent.WorkflowExecutionId,
            item);
        var (schemaVersion, content) = serializer.Serialize(ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind, envelope);
        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                item.OutboxItemId,
                schemaVersion,
                content),
            cancellationToken);
    }

    private RuntimePostCommitOutboxItem Map(DocumentEnvelope envelope) =>
        serializer.Deserialize<OutboxEnvelope>(envelope).Item;

    // Two pending saves of the same item must be idempotent. Comparing the serialized intent under the shared
    // options is equivalent to the in-memory store's field-by-field comparison and avoids drifting from the
    // intent's shape over time.
    private bool IsSamePendingIntent(RuntimePostCommitOutboxItem existing, RuntimePostCommitOutboxItem item) =>
        existing.Status == RuntimePostCommitOutboxStatus.Pending
        && StringComparer.Ordinal.Equals(
            serializer.SerializeForComparison(existing.Intent),
            serializer.SerializeForComparison(item.Intent));

    private static bool IsDeliverable(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxQuery query)
    {
        if (query.WorkflowExecutionId is not null && !StringComparer.Ordinal.Equals(item.Intent.WorkflowExecutionId, query.WorkflowExecutionId))
            return false;
        if (query.IntentKind is not null && !StringComparer.Ordinal.Equals(item.Intent.Kind, query.IntentKind))
            return false;
        if (item.AvailableAt is { } availableAt && availableAt > query.Now)
            return false;
        if (item.Status == RuntimePostCommitOutboxStatus.Pending)
            return true;
        if (item.Status == RuntimePostCommitOutboxStatus.FailedRetryable)
            return item.RetryPolicy.MaxAttempts > 0 && item.DeliveryAttemptCount < item.RetryPolicy.MaxAttempts;
        return false;
    }

    private static RuntimePostCommitOutboxStatus NormalizeDeliveryStatus(
        RuntimePostCommitOutboxItem existing,
        RuntimePostCommitOutboxStatus status,
        int deliveryAttemptCount)
    {
        if (status != RuntimePostCommitOutboxStatus.FailedRetryable)
            return status;
        return deliveryAttemptCount >= existing.RetryPolicy.MaxAttempts
            ? RuntimePostCommitOutboxStatus.FailedFinal
            : RuntimePostCommitOutboxStatus.FailedRetryable;
    }

    private static DateTimeOffset NextRetryAvailableAt(RuntimePostCommitOutboxItem existing, DateTimeOffset recordedAt) =>
        existing.RetryPolicy.Delay is { } delay ? recordedAt.Add(delay) : recordedAt;

    // The constant collection partition lets unfiltered GetDeliverable scans use a keyword equality index
    // instead of a provider-wide scan, mirroring the other list-capable bridges.
    private sealed record OutboxEnvelope(string Collection, string WorkflowExecutionId, RuntimePostCommitOutboxItem Item);
}
