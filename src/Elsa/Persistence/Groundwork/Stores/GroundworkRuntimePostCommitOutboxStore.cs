using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IRuntimePostCommitOutboxStore"/> for the Groundwork bridge, backed by the portable
/// <see cref="IDocumentStore"/>.
/// </summary>
/// <remarks>
/// This bridge deliberately uses the portable document store rather than Groundwork's operational
/// <c>IOutboxStore</c>. Elsa supplies deterministic outbox identities that must participate in the runtime
/// checkpoint transaction, then adds its own owner/fencing-token/visibility claim state to those documents.
/// Keeping each item on the shared document substrate preserves that atomic checkpoint boundary while allowing
/// claim-aware delivery and final dispatch-failure projection to use optimistic or cross-unit transactions.
/// </remarks>
public sealed class GroundworkRuntimePostCommitOutboxStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null,
    IPersistenceAccessContextAccessor? accessContextAccessor = null) :
    IRuntimePostCommitOutboxStore,
    IPostCommitOutboxLookupStore,
    IRuntimePostCommitOutboxClaimStore,
    IRuntimePostCommitOutboxClaimCompletionStore
{
    private const int CandidatePageSize = 100;
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

        return await QueryCandidatesAsync(query, includeExpiredClaims: false, query.Limit, cancellationToken);
    }

    public async ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
        string outboxItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        cancellationToken.ThrowIfCancellationRequested();
        return (await LoadAsync(outboxItemId, cancellationToken))?.Item;
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
        if (existing.Item.Status == RuntimePostCommitOutboxStatus.Delivering || existing.Item.DeliveryFencingToken > 0)
            throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is claimed; its owner and fencing token are required.");

        var deliveryAttemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(existing.Item.DeliveryAttemptCount);
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

    public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Provider queries establish the tenant boundary. Each selected item is then loaded and saved with
        // its exact optimistic version, so competing processes cannot both own the same fencing token.
        var query = new RuntimePostCommitOutboxQuery(
            request.Now,
            request.Limit,
            request.WorkflowExecutionId,
            intentKind: request.IntentKind);
        var candidates = await QueryCandidatesAsync(query, includeExpiredClaims: true, request.Limit, cancellationToken);
        var claims = new List<RuntimePostCommitOutboxClaim>(request.Limit);
        foreach (var candidate in candidates)
        {
            if (claims.Count == request.Limit)
                break;

            var loaded = await LoadAsync(candidate.OutboxItemId, cancellationToken);
            if (loaded is null || !RuntimePostCommitOutboxClaimTransitions.CanClaim(loaded.Item, request))
                continue;

            var claim = RuntimePostCommitOutboxClaimTransitions.Claim(loaded.Item, request);
            var result = await SaveAsync(claim.Item, loaded.Version, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
            {
                claims.Add(claim);
                continue;
            }

            if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
            {
                throw new InvalidOperationException(
                    $"Groundwork rejected the claim for post-commit outbox item '{candidate.OutboxItemId}' with status '{result.Status}'.");
            }
        }

        return claims;
    }

    public async ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var loaded = await LoadAsync(claim.OutboxItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Post-commit outbox item '{claim.OutboxItemId}' was not found.");
        var completed = RuntimePostCommitOutboxClaimTransitions.Complete(loaded.Item, claim, result);
        var writeResult = await SaveAsync(completed, loaded.Version, cancellationToken);
        if (writeResult.Status == DocumentStoreWriteStatus.Saved)
            return;
        if (writeResult.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
        {
            var current = await LoadAsync(claim.OutboxItemId, cancellationToken);
            if (current is not null)
                RuntimePostCommitOutboxClaimTransitions.Complete(current.Item, claim, result);
        }

        throw new InvalidOperationException(
            $"Groundwork rejected the claimed delivery result for post-commit outbox item '{claim.OutboxItemId}' with status '{writeResult.Status}'.");
    }

    public async ValueTask CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();
        if (store.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
        {
            throw new InvalidOperationException(
                "Groundwork cannot atomically complete a post-commit outbox claim because the active document store does not support cross-unit transactions.");
        }
        if (completion.WorkflowDispatch is not null && accessContextAccessor is null)
        {
            throw new InvalidOperationException(
                "Atomic workflow-dispatch failure projection requires the active persistence access context.");
        }

        await using var unitOfWork = await store.BeginAsync(
            DocumentCommitScope.Of(
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind),
            cancellationToken);
        var transactionalStore = new GroundworkDocumentUnitOfWorkStore(store, unitOfWork);
        var transactionalOutbox = new GroundworkRuntimePostCommitOutboxStore(
            transactionalStore,
            serializer,
            accessContextAccessor: accessContextAccessor);

        var loaded = await transactionalOutbox.LoadAsync(completion.Claim.OutboxItemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Post-commit outbox item '{completion.Claim.OutboxItemId}' was not found.");
        var completed = RuntimePostCommitOutboxClaimTransitions.Complete(
            loaded.Item,
            completion.Claim,
            completion.DeliveryResult);
        var writeResult = await transactionalOutbox.SaveAsync(completed, loaded.Version, cancellationToken);
        if (writeResult.Status != DocumentStoreWriteStatus.Saved)
        {
            throw new InvalidOperationException(
                $"Groundwork rejected the claimed delivery result for post-commit outbox item '{completion.Claim.OutboxItemId}' with status '{writeResult.Status}'.");
        }

        if (completion.WorkflowDispatch is { } workflowDispatch)
        {
            if (completed.Status != RuntimePostCommitOutboxStatus.FailedFinal ||
                workflowDispatch.Status != WorkflowDispatchStatus.DispatchFailed)
            {
                throw new InvalidOperationException(
                    "An atomic workflow-dispatch projection is valid only for a final outbox failure and DispatchFailed lifecycle state.");
            }
            if (!completion.Claim.Item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId) ||
                !StringComparer.Ordinal.Equals(dispatchId, workflowDispatch.DispatchId))
            {
                throw new InvalidOperationException(
                    "The workflow-dispatch failure projection does not match the claimed child-start intent.");
            }

            var dispatchStore = new GroundworkWorkflowDispatchStore(
                transactionalStore,
                serializer,
                accessContextAccessor!);
            await dispatchStore.SaveAsync(workflowDispatch, cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
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

    private async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> QueryCandidatesAsync(
        RuntimePostCommitOutboxQuery query,
        bool includeExpiredClaims,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var (queryIdentity, clauses) = query.WorkflowExecutionId is { } workflowExecutionId
            ? (ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
                (IReadOnlyList<DocumentQueryClause>)
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflowExecutionId))])
            : (ElsaRuntimeStorageManifest.ListAllQuery,
                (IReadOnlyList<DocumentQueryClause>)
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    ElsaRuntimeStorageManifest.CollectionField,
                    ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind))]);
        var candidates = new List<RuntimePostCommitOutboxItem>(maximumResults);
        var skip = 0;
        while (true)
        {
            var documentQuery = new DocumentQuery(
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                queryIdentity,
                clauses,
                skip: skip,
                take: CandidatePageSize);
            var envelopes = (await BoundedStore.QueryAsync(documentQuery, cancellationToken)).Documents;
            candidates.AddRange(envelopes
                .Select(Map)
                .Where(item => includeExpiredClaims
                    ? IsClaimCandidate(item, query)
                    : IsDeliverable(item, query)));
            if (candidates.Count > maximumResults)
            {
                candidates = candidates
                    .OrderBy(item => item.AvailableAt ?? DateTimeOffset.MinValue)
                    .ThenBy(item => item.RecordedAt)
                    .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal)
                    .Take(maximumResults)
                    .ToList();
            }
            if (envelopes.Count < CandidatePageSize)
                break;
            skip += envelopes.Count;
        }

        return candidates
            .OrderBy(item => item.AvailableAt ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.RecordedAt)
            .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal)
            .Take(maximumResults)
            .ToArray();
    }

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
            return !item.RetryPolicy.IsExhaustedAfterAttempt(item.DeliveryAttemptCount);
        return false;
    }

    private static bool IsClaimCandidate(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxQuery query)
    {
        var request = new RuntimePostCommitOutboxClaimRequest(
            "candidate-filter",
            query.Now,
            TimeSpan.FromTicks(1),
            query.Limit,
            query.WorkflowExecutionId,
            query.IntentKind);
        return RuntimePostCommitOutboxClaimTransitions.CanClaim(item, request);
    }

    private static RuntimePostCommitOutboxStatus NormalizeDeliveryStatus(
        RuntimePostCommitOutboxItem existing,
        RuntimePostCommitOutboxStatus status,
        int deliveryAttemptCount)
    {
        if (status != RuntimePostCommitOutboxStatus.FailedRetryable)
            return status;
        return existing.RetryPolicy.IsExhaustedAfterAttempt(deliveryAttemptCount)
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
