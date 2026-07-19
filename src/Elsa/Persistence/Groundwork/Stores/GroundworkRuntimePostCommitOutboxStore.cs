using System.Globalization;
using System.Text.Json.Serialization;
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
    IRuntimePostCommitOutboxClaimCompletionStore,
    IWorkflowDispatchRedriveStore
{
    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    internal IDocumentStore DocumentStore => store;

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

        return await QueryCandidatesAsync(query, CandidateSelection.Deliverable, query.Limit, cancellationToken);
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
        var candidates = await QueryCandidatesAsync(query, CandidateSelection.Claimable, request.Limit, cancellationToken);
        var claims = new List<RuntimePostCommitOutboxClaim>();
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
                ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind),
            cancellationToken);
        var transactionalStore = new GroundworkDocumentUnitOfWorkStore(store, unitOfWork);
        var transactionalOutbox = new GroundworkRuntimePostCommitOutboxStore(
            transactionalStore,
            serializer,
            accessContextAccessor: accessContextAccessor);

        var loaded = await transactionalOutbox.LoadAsync(completion.Claim.OutboxItemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Post-commit outbox item '{completion.Claim.OutboxItemId}' was not found.");
        // Always validate the current claim/fence before lifecycle precedence. A stale claimant cannot acknowledge
        // a newer redrive generation merely because the deterministic child is now visible.
        var completed = RuntimePostCommitOutboxClaimTransitions.Complete(
            loaded.Item,
            completion.Claim,
            completion.DeliveryResult);
        GroundworkWorkflowDispatchStore? dispatchStore = null;
        WorkflowDispatchRecord? winningDispatch = null;
        var admissionWins = false;
        if (completion.WorkflowDispatch is { } projectedDispatch)
        {
            dispatchStore = new GroundworkWorkflowDispatchStore(
                transactionalStore,
                serializer,
                accessContextAccessor!);
            var existingDispatch = await dispatchStore.FindAsync(projectedDispatch.DispatchId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Workflow dispatch '{projectedDispatch.DispatchId}' was not found in the atomic completion transaction.");
            accessContextAccessor!.Current.EnsureTenantScope(existingDispatch.TenantId);
            var executionStore = new GroundworkWorkflowExecutionStateStore(
                transactionalStore,
                serializer,
                accessContextAccessor);
            var childExecution = await executionStore.FindAsync(
                existingDispatch.ChildWorkflowExecutionId,
                cancellationToken);
            winningDispatch = WorkflowDispatchLifecycle.ResolveSuccessfulChildDelivery(
                existingDispatch,
                childExecution,
                completion.DeliveryResult.RecordedAt);
            admissionWins = winningDispatch is not null;
            if (admissionWins)
            {
                completed = RuntimePostCommitOutboxClaimTransitions.Complete(
                    loaded.Item,
                    completion.Claim,
                    new RuntimePostCommitOutboxDeliveryResult(
                        completion.Claim.OutboxItemId,
                        RuntimePostCommitOutboxStatus.Delivered,
                        completion.DeliveryResult.RecordedAt));
            }
            else
            {
                winningDispatch = projectedDispatch;
            }
        }
        var writeResult = await transactionalOutbox.SaveAsync(completed, loaded.Version, cancellationToken);
        if (writeResult.Status != DocumentStoreWriteStatus.Saved)
        {
            throw new InvalidOperationException(
                $"Groundwork rejected the claimed delivery result for post-commit outbox item '{completion.Claim.OutboxItemId}' with status '{writeResult.Status}'.");
        }

        if (winningDispatch is { } workflowDispatch)
        {
            if (!admissionWins &&
                (completed.Status != RuntimePostCommitOutboxStatus.FailedFinal ||
                 workflowDispatch.Status != WorkflowDispatchStatus.DispatchFailed))
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

            await dispatchStore!.SaveAsync(workflowDispatch, cancellationToken);
        }
        if (!admissionWins && completion.FollowUpOutboxItem is { } followUpOutboxItem)
        {
            if (StringComparer.Ordinal.Equals(followUpOutboxItem.OutboxItemId, completion.Claim.OutboxItemId))
                throw new InvalidOperationException("A post-commit follow-up cannot replace the claimed outbox item.");
            await transactionalOutbox.SavePendingAsync(followUpOutboxItem, cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
        WorkflowDispatchRedriveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (accessContextAccessor is null)
            throw new InvalidOperationException("Workflow-dispatch redrive requires the active persistence access context.");
        if (store.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
        {
            throw new InvalidOperationException(
                "Groundwork cannot atomically redrive a workflow dispatch because the active document store does not support cross-unit transactions.");
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
        var transactionalDispatch = new GroundworkWorkflowDispatchStore(
            transactionalStore,
            serializer,
            accessContextAccessor);

        var dispatch = await transactionalDispatch.FindAsync(request.DispatchId, cancellationToken);
        if (dispatch is not null)
            accessContextAccessor.Current.EnsureTenantScope(dispatch.TenantId);
        var deadLetterId = dispatch is null ? null : WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch);
        var loadedDeadLetter = deadLetterId is null
            ? null
            : await transactionalOutbox.LoadAsync(deadLetterId, cancellationToken);
        var transition = WorkflowDispatchRedriveTransitions.Evaluate(request, dispatch, loadedDeadLetter?.Item);
        if (!transition.HasMutation)
            return transition.Result;

        var outboxWrite = await transactionalOutbox.SaveAsync(
            transition.OutboxItem!,
            loadedDeadLetter!.Version,
            cancellationToken);
        if (outboxWrite.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new InvalidOperationException($"Workflow dispatch redrive '{request.DispatchId}' lost its outbox fence.");
        if (outboxWrite.Status != DocumentStoreWriteStatus.Saved)
        {
            throw new InvalidOperationException(
                $"Groundwork rejected workflow dispatch redrive outbox '{transition.OutboxItem!.OutboxItemId}' with status '{outboxWrite.Status}'.");
        }
        if (!await transactionalDispatch.TrySaveRedriveAsync(dispatch!, transition.WorkflowDispatch!, cancellationToken))
            throw new InvalidOperationException($"Workflow dispatch redrive '{request.DispatchId}' lost its dispatch fence.");

        await unitOfWork.CommitAsync(cancellationToken);
        return transition.Result;
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
        var projectionValue = RuntimePostCommitOutboxIdentity.CreateProjectionValue(item.OutboxItemId);
        var persistedItem = StringComparer.Ordinal.Equals(projectionValue, item.OutboxItemId)
            ? item
            : WithOutboxItemId(item, projectionValue);
        var envelope = CreateEnvelope(persistedItem) with
        {
            LogicalOutboxItemId = StringComparer.Ordinal.Equals(projectionValue, item.OutboxItemId)
                ? null
                : item.OutboxItemId
        };
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

    private RuntimePostCommitOutboxItem Map(DocumentEnvelope envelope)
    {
        var outbox = serializer.Deserialize<OutboxEnvelope>(envelope);
        return outbox.LogicalOutboxItemId is { } logicalOutboxItemId
            ? WithOutboxItemId(outbox.Item, logicalOutboxItemId)
            : outbox.Item;
    }

    private static RuntimePostCommitOutboxItem WithOutboxItemId(
        RuntimePostCommitOutboxItem item,
        string outboxItemId) =>
        new(
            outboxItemId,
            item.Intent,
            item.Status,
            item.RecordedAt,
            item.AvailableAt,
            item.RetryPolicy,
            item.DeliveryAttemptCount,
            item.DeliveringOwnerId,
            item.DeliveryStartedAt,
            item.DeliveredAt,
            item.LastFailureMessage,
            item.Metadata,
            item.DeliveryFencingToken,
            item.DeliveryVisibleAfter);

    private async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> QueryCandidatesAsync(
        RuntimePostCommitOutboxQuery query,
        CandidateSelection selection,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var (candidateAtField, queryIdentity) = SelectCandidateRoute(query, selection);
        var clauses = new List<DocumentQueryClause>();
        if (query.WorkflowExecutionId is { } workflowExecutionId)
            clauses.Add(Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflowExecutionId));
        if (query.IntentKind is { } intentKind)
            clauses.Add(Equal(ElsaRuntimeStorageManifest.PostCommitOutboxIntentKindField, intentKind));
        clauses.Add(LessThanOrEqual(candidateAtField, query.Now.ToString("O", CultureInfo.InvariantCulture)));

        var order = new List<DocumentQueryOrder>();
        if (query.WorkflowExecutionId is not null)
            order.Add(new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionIdField));
        if (query.IntentKind is not null)
            order.Add(new DocumentQueryOrder(ElsaRuntimeStorageManifest.PostCommitOutboxIntentKindField));
        order.Add(new DocumentQueryOrder(candidateAtField));
        order.Add(new DocumentQueryOrder(ElsaRuntimeStorageManifest.PostCommitOutboxRecordedAtField));
        order.Add(new DocumentQueryOrder(ElsaRuntimeStorageManifest.PostCommitOutboxItemIdField));

        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                queryIdentity,
                clauses,
                order,
                take: Math.Min(maximumResults, RuntimeStorePageRequest.MaximumLimit)),
            cancellationToken);
        return result.Documents.Select(Map).ToArray();
    }

    private static (string CandidateAtField, string QueryIdentity) SelectCandidateRoute(
        RuntimePostCommitOutboxQuery query,
        CandidateSelection selection) =>
        (selection, query.WorkflowExecutionId is not null, query.IntentKind is not null) switch
        {
            (CandidateSelection.Deliverable, false, false) =>
                (ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField, ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxQuery),
            (CandidateSelection.Deliverable, true, false) =>
                (ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField, ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowQuery),
            (CandidateSelection.Deliverable, false, true) =>
                (ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField, ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByIntentKindQuery),
            (CandidateSelection.Deliverable, true, true) =>
                (ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField, ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowAndIntentKindQuery),
            (CandidateSelection.Claimable, false, false) =>
                (ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField, ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxQuery),
            (CandidateSelection.Claimable, true, false) =>
                (ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField, ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxByWorkflowQuery),
            (CandidateSelection.Claimable, false, true) =>
                (ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField, ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxByIntentKindQuery),
            (CandidateSelection.Claimable, true, true) =>
                (ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField, ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxByWorkflowAndIntentKindQuery),
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, null)
        };

    private static DocumentQueryClause Equal(string path, string? value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    private static DocumentQueryClause LessThanOrEqual(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(path, value));

    private enum CandidateSelection
    {
        Deliverable,
        Claimable
    }

    // Two pending saves of the same item must be idempotent. Comparing the serialized intent under the shared
    // options is equivalent to the in-memory store's field-by-field comparison and avoids drifting from the
    // intent's shape over time.
    private bool IsSamePendingIntent(RuntimePostCommitOutboxItem existing, RuntimePostCommitOutboxItem item) =>
        existing.Status == RuntimePostCommitOutboxStatus.Pending
        && item.Status == RuntimePostCommitOutboxStatus.Pending
        && StringComparer.Ordinal.Equals(
            serializer.SerializeForComparison(existing.Intent),
            serializer.SerializeForComparison(item.Intent))
        && existing.RecordedAt == item.RecordedAt
        && existing.AvailableAt == item.AvailableAt
        && existing.DeliveryAttemptCount == item.DeliveryAttemptCount
        && existing.DeliveryFencingToken == item.DeliveryFencingToken
        && existing.RetryPolicy.IsEquivalentTo(item.RetryPolicy)
        && MetadataEquals(existing.Metadata, item.Metadata);

    private static bool MetadataEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(item => right.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));

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

    private static OutboxEnvelope CreateEnvelope(RuntimePostCommitOutboxItem item)
    {
        DateTimeOffset? deliverableAt = item.Status == RuntimePostCommitOutboxStatus.Pending ||
                                        (item.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
                                         !item.RetryPolicy.IsExhaustedAfterAttempt(item.DeliveryAttemptCount))
            ? item.AvailableAt ?? DateTimeOffset.MinValue
            : null;
        var claimableAt = item.Status == RuntimePostCommitOutboxStatus.Delivering
            ? item.DeliveryVisibleAfter
            : deliverableAt;
        return new OutboxEnvelope(
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            item.Intent.WorkflowExecutionId,
            deliverableAt,
            claimableAt,
            item);
    }

    private sealed record LoadedOutboxItem(RuntimePostCommitOutboxItem Item, long Version);

    private sealed record OutboxEnvelope(
        string Collection,
        string WorkflowExecutionId,
        DateTimeOffset? DeliverableAt,
        DateTimeOffset? ClaimableAt,
        RuntimePostCommitOutboxItem Item,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? LogicalOutboxItemId = null);

}
