using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
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
public sealed class GroundworkRuntimePostCommitOutboxStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null) : IRuntimePostCommitOutboxStore
{
    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("Post-commit outbox queries require an admitted bounded document-store runtime.");

    public async ValueTask SavePendingAsync(RuntimePostCommitOutboxItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");

        var existing = await LoadAsync(item.OutboxItemId, cancellationToken);
        if (existing is not null)
        {
            if (IsSamePendingIntent(existing.Item, item))
                return;
            throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
        }

        var result = await SaveAsync(item, expectedVersion: 0, cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return;
        if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new InvalidOperationException($"Groundwork rejected post-commit outbox item '{item.OutboxItemId}' with status '{result.Status}'.");

        existing = await LoadAsync(item.OutboxItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' conflicted during creation but could not be reloaded.");
        if (!IsSamePendingIntent(existing.Item, item))
            throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
    }

    public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.OwnerId is not null)
            throw new NotSupportedException("The Groundwork post-commit outbox store does not implement delivery ownership filtering.");

        var documentQuery = query.WorkflowExecutionId is { } workflowExecutionId
            ? new DocumentQuery(
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflowExecutionId))])
            : new DocumentQuery(
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                ElsaRuntimeStorageManifest.ListAllQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    ElsaRuntimeStorageManifest.CollectionField,
                    ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind))]);
        var envelopes = (await BoundedStore.QueryAsync(documentQuery, cancellationToken)).Documents;

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
        if (existing.Item.IsTerminal)
            throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is already terminal.");

        var deliveryAttemptCount = existing.Item.DeliveryAttemptCount + 1;
        var status = NormalizeDeliveryStatus(existing.Item, result.Status, deliveryAttemptCount);
        DateTimeOffset? availableAt = status == RuntimePostCommitOutboxStatus.FailedRetryable
            ? NextRetryAvailableAt(existing.Item, result.RecordedAt)
            : null;

        var updated = new RuntimePostCommitOutboxItem(
            outboxItemId: existing.Item.OutboxItemId,
            intent: existing.Item.Intent,
            status: status,
            recordedAt: existing.Item.RecordedAt,
            availableAt: availableAt,
            retryPolicy: existing.Item.RetryPolicy,
            deliveryAttemptCount: deliveryAttemptCount,
            deliveringOwnerId: null,
            deliveryStartedAt: null,
            deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
            lastFailureMessage: result.FailureMessage,
            metadata: existing.Item.Metadata);

        var writeResult = await SaveAsync(updated, existing.Version, cancellationToken);
        if (writeResult.Status == DocumentStoreWriteStatus.Saved)
            return;

        if (writeResult.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
            await LoadAsync(result.OutboxItemId, cancellationToken);
        throw new InvalidOperationException($"Groundwork rejected the delivery result for post-commit outbox item '{result.OutboxItemId}' with status '{writeResult.Status}'.");
    }

    private async ValueTask<LoadedOutboxItem?> LoadAsync(string outboxItemId, CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            GroundworkPhysicalDocumentId.FromLogicalId(outboxItemId),
            cancellationToken);
        if (envelope is null)
            return null;

        var item = Map(envelope);
        if (!StringComparer.Ordinal.Equals(item.OutboxItemId, outboxItemId))
            throw new InvalidOperationException($"Groundwork physical document identity collision detected for post-commit outbox item '{outboxItemId}'.");
        return new LoadedOutboxItem(item, envelope.Version);
    }

    private async ValueTask<DocumentStoreWriteResult> SaveAsync(
        RuntimePostCommitOutboxItem item,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var envelope = new OutboxEnvelope(
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            item.Intent.WorkflowExecutionId,
            item);
        var (schemaVersion, content) = serializer.Serialize(ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind, envelope);
        return await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                GroundworkPhysicalDocumentId.FromLogicalId(item.OutboxItemId),
                schemaVersion,
                content,
                expectedVersion),
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
    private sealed record LoadedOutboxItem(RuntimePostCommitOutboxItem Item, long Version);

    private sealed record OutboxEnvelope(string Collection, string WorkflowExecutionId, RuntimePostCommitOutboxItem Item);
}
