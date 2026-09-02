using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>
/// Current-only Groundwork v2 implementation of Elsa's post-commit outbox contracts.
/// </summary>
/// <remarks>
/// Every operation uses one admitted public v2 storage session. Claim transitions are optimistic CAS writes over
/// the row revision plus the owner and fencing token in the row content. Atomic completion and dispatch redrive use
/// one exact public v2 unit of work; no v1 document bridge, migration, fallback, or dual-write path is present.
/// </remarks>
public sealed class GroundworkV2RuntimePostCommitOutboxStore :
    IRuntimePostCommitOutboxStore,
    IPostCommitOutboxLookupStore,
    IRuntimePostCommitOutboxClaimStore,
    IRuntimePostCommitOutboxClaimCompletionStore,
    IWorkflowDispatchRedriveStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit outboxUnit;
    private readonly StorageUnit dispatchUnit;
    private readonly StorageUnit executionUnit;

    public GroundworkV2RuntimePostCommitOutboxStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        outboxUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind, targetName);
        dispatchUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind, targetName);
        executionUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, targetName);
    }

    public ValueTask SavePendingAsync(
        RuntimePostCommitOutboxItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");

        var session = OpenOutbox();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(item.OutboxItemId));
        var existing = session.Read(key);
        if (existing is not null)
        {
            var current = ReadOutbox(existing, item.OutboxItemId);
            if (GroundworkV2PostCommitOutboxStorageConventions.PendingItemsEquivalent(current, item))
                return ValueTask.CompletedTask;
            throw new InvalidOperationException(
                $"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
        }

        var result = session.Insert(
            GroundworkV2PostCommitOutboxStorageConventions.Values(item),
            WriteOptions.CreateOnly);
        if (IsSaved(result.Status))
            return ValueTask.CompletedTask;
        if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
        {
            throw new InvalidOperationException(
                $"Groundwork rejected post-commit outbox item '{item.OutboxItemId}' with status '{result.Status}'.");
        }

        var winner = session.Read(key)
                     ?? throw new InvalidOperationException(
                         $"Post-commit outbox item '{item.OutboxItemId}' conflicted during creation but could not be reloaded.");
        if (!GroundworkV2PostCommitOutboxStorageConventions.PendingItemsEquivalent(
                ReadOutbox(winner, item.OutboxItemId),
                item))
        {
            throw new InvalidOperationException(
                $"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
        string outboxItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = OpenOutbox().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(outboxItemId)));
        return ValueTask.FromResult(entry is null ? null : ReadOutbox(entry, outboxItemId));
    }

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
        RuntimePostCommitOutboxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.OwnerId is not null)
            throw new NotSupportedException("The Groundwork v2 post-commit outbox store does not implement delivery ownership filtering.");

        return ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxItem>>(
            QueryCandidates(query, CandidateSelection.Deliverable, query.Limit, cancellationToken));
    }

    public ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var session = OpenOutbox();
        var entry = session.Read(GroundworkRuntimeRowStore.Key(
                        GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(result.OutboxItemId)))
                    ?? throw new InvalidOperationException(
                        $"Post-commit outbox item '{result.OutboxItemId}' was not found.");
        var existing = ReadOutbox(entry, result.OutboxItemId);
        if (existing.IsTerminal)
            throw new InvalidOperationException(
                $"Post-commit outbox item '{result.OutboxItemId}' is already terminal.");
        if (existing.Status == RuntimePostCommitOutboxStatus.Delivering || existing.DeliveryFencingToken > 0)
        {
            throw new InvalidOperationException(
                $"Post-commit outbox item '{result.OutboxItemId}' is claimed; its owner and fencing token are required.");
        }

        var attemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(existing.DeliveryAttemptCount);
        var status = NormalizeDeliveryStatus(existing, result.Status, attemptCount);
        DateTimeOffset? availableAt = status == RuntimePostCommitOutboxStatus.FailedRetryable
            ? result.RecordedAt.Add(existing.RetryPolicy.Delay ?? TimeSpan.Zero)
            : null;
        var updated = WithDeliveryState(
            existing,
            status,
            availableAt,
            attemptCount,
            deliveringOwnerId: null,
            deliveryStartedAt: null,
            deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
            result.FailureMessage,
            existing.DeliveryFencingToken,
            deliveryVisibleAfter: null);
        var write = ConditionalUpsert(session, GroundworkV2PostCommitOutboxStorageConventions.Values(updated), entry);
        if (write.Status == WriteOutcomeStatus.ConcurrencyConflict)
            throw new InvalidOperationException(
                $"Groundwork rejected the delivery result for post-commit outbox item '{result.OutboxItemId}' with a concurrency conflict.");
        if (!IsSaved(write.Status))
            throw new InvalidOperationException(
                $"Groundwork rejected the delivery result for post-commit outbox item '{result.OutboxItemId}' with status '{write.Status}'.");

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = QueryCandidates(
            new RuntimePostCommitOutboxQuery(
                request.Now,
                request.Limit,
                request.WorkflowExecutionId,
                intentKind: request.IntentKind),
            CandidateSelection.Claimable,
            request.Limit,
            cancellationToken);
        var session = OpenOutbox();
        var claims = new List<RuntimePostCommitOutboxClaim>(Math.Min(request.Limit, candidates.Count));
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (claims.Count == request.Limit)
                break;

            var entry = session.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(candidate.OutboxItemId)));
            if (entry is null)
                continue;
            var current = ReadOutbox(entry, candidate.OutboxItemId);
            if (!RuntimePostCommitOutboxClaimTransitions.CanClaim(current, request))
                continue;

            var claim = RuntimePostCommitOutboxClaimTransitions.Claim(current, request);
            var write = ConditionalUpsert(session, GroundworkV2PostCommitOutboxStorageConventions.Values(claim.Item), entry);
            if (write.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound)
                continue;
            if (!IsSaved(write.Status))
            {
                throw new InvalidOperationException(
                    $"Groundwork rejected the claim for post-commit outbox item '{candidate.OutboxItemId}' with status '{write.Status}'.");
            }

            claims.Add(claim);
        }

        return ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxClaim>>(claims);
    }

    public ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var session = OpenOutbox();
        var entry = session.Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(claim.OutboxItemId)));
        if (entry is null)
            throw new InvalidOperationException($"Post-commit outbox item '{claim.OutboxItemId}' was not found.");
        var current = ReadOutbox(entry, claim.OutboxItemId);
        var completed = RuntimePostCommitOutboxClaimTransitions.Complete(current, claim, result);
        var write = ConditionalUpsert(session, GroundworkV2PostCommitOutboxStorageConventions.Values(completed), entry);
        if (write.Status == WriteOutcomeStatus.ConcurrencyConflict)
        {
            // Re-run the shared transition against the current row to preserve the public stale-claim exception.
            var latest = session.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(claim.OutboxItemId)));
            if (latest is not null)
                _ = RuntimePostCommitOutboxClaimTransitions.Complete(ReadOutbox(latest, claim.OutboxItemId), claim, result);
            throw new InvalidOperationException(
                $"Groundwork rejected the claimed delivery result for post-commit outbox item '{claim.OutboxItemId}' with a concurrency conflict.");
        }
        if (!IsSaved(write.Status))
        {
            throw new InvalidOperationException(
                $"Groundwork rejected the claimed delivery result for post-commit outbox item '{claim.OutboxItemId}' with status '{write.Status}'.");
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();

        using var unitOfWork = BeginAtomicUnitOfWork(
            [
                ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind
            ]);
        var outbox = unitOfWork.OpenSession(outboxUnit);
        var dispatches = unitOfWork.OpenSession(dispatchUnit);
        var executions = unitOfWork.OpenSession(executionUnit);

        var entry = outbox.Read(GroundworkRuntimeRowStore.Key(
                        GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(completion.Claim.OutboxItemId)))
                    ?? throw new InvalidOperationException(
                        $"Post-commit outbox item '{completion.Claim.OutboxItemId}' was not found.");
        var current = ReadOutbox(entry, completion.Claim.OutboxItemId);
        var completed = RuntimePostCommitOutboxClaimTransitions.Complete(
            current,
            completion.Claim,
            completion.DeliveryResult);

        WorkflowDispatchRecord? winningDispatch = null;
        long? winningDispatchVersion = null;
        var admissionWins = false;
        if (completion.WorkflowDispatch is { } projectedDispatch)
        {
            var dispatchEntry = dispatches.Read(GroundworkRuntimeRowStore.Key(
                                    GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(projectedDispatch.DispatchId)))
                               ?? throw new InvalidOperationException(
                                   $"Workflow dispatch '{projectedDispatch.DispatchId}' was not found in the atomic completion unit of work.");
            var existingDispatch = ReadDispatch(dispatchEntry, projectedDispatch.DispatchId);
            accessContextAccessor.Current.EnsureTenantScope(existingDispatch.TenantId);
            var childEntry = executions.Read(GroundworkRuntimeRowStore.Key(existingDispatch.ChildWorkflowExecutionId));
            var childExecution = childEntry is null ? null : ReadExecution(childEntry, existingDispatch.ChildWorkflowExecutionId);
            winningDispatch = WorkflowDispatchLifecycle.ResolveSuccessfulChildDelivery(
                existingDispatch,
                childExecution,
                completion.DeliveryResult.RecordedAt);
            admissionWins = winningDispatch is not null;
            if (admissionWins)
            {
                completed = RuntimePostCommitOutboxClaimTransitions.Complete(
                    current,
                    completion.Claim,
                    new RuntimePostCommitOutboxDeliveryResult(
                        completion.Claim.OutboxItemId,
                        RuntimePostCommitOutboxStatus.Delivered,
                        completion.DeliveryResult.RecordedAt));
                winningDispatchVersion = dispatchEntry.Version ?? throw new InvalidDataException(
                    $"Workflow dispatch '{projectedDispatch.DispatchId}' did not expose an optimistic revision.");
            }
            else
            {
                WorkflowDispatchLifecycle.ValidateTransition(existingDispatch, projectedDispatch);
                winningDispatch = projectedDispatch;
                winningDispatchVersion = dispatchEntry.Version ?? throw new InvalidDataException(
                    $"Workflow dispatch '{projectedDispatch.DispatchId}' did not expose an optimistic revision.");
            }
        }

        StageUpsert(
            unitOfWork,
            outboxUnit,
            GroundworkV2PostCommitOutboxStorageConventions.Values(completed),
            entry.Version ?? throw new InvalidDataException(
                $"Post-commit outbox item '{completion.Claim.OutboxItemId}' did not expose an optimistic revision."));

        if (winningDispatch is not null)
        {
            if (!admissionWins &&
                (completed.Status != RuntimePostCommitOutboxStatus.FailedFinal ||
                 winningDispatch.Status != WorkflowDispatchStatus.DispatchFailed))
            {
                throw new InvalidOperationException(
                    "An atomic workflow-dispatch projection is valid only for a final outbox failure and DispatchFailed lifecycle state.");
            }
            if (!completion.Claim.Item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId) ||
                !StringComparer.Ordinal.Equals(dispatchId, winningDispatch.DispatchId))
            {
                throw new InvalidOperationException(
                    "The workflow-dispatch failure projection does not match the claimed child-start intent.");
            }

            StageUpsert(
                unitOfWork,
                dispatchUnit,
                GroundworkV2WorkflowDispatchStorageConventions.Values(winningDispatch),
                winningDispatchVersion!.Value);
        }

        if (!admissionWins && completion.FollowUpOutboxItem is { } followUp)
        {
            if (StringComparer.Ordinal.Equals(followUp.OutboxItemId, completion.Claim.OutboxItemId))
                throw new InvalidOperationException("A post-commit follow-up cannot replace the claimed outbox item.");
            StagePendingIfAbsent(unitOfWork, outbox, followUp);
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
        WorkflowDispatchRedriveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();

        using var unitOfWork = BeginAtomicUnitOfWork(
            [
                ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind
            ]);
        var outbox = unitOfWork.OpenSession(outboxUnit);
        var dispatches = unitOfWork.OpenSession(dispatchUnit);
        var dispatchEntry = dispatches.Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(request.DispatchId)));
        var dispatch = dispatchEntry is null ? null : ReadDispatch(dispatchEntry, request.DispatchId);
        if (dispatch is not null)
            accessContextAccessor.Current.EnsureTenantScope(dispatch.TenantId);

        var deadLetterId = dispatch is null ? null : WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch);
        var deadLetterEntry = deadLetterId is null
            ? null
            : outbox.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(deadLetterId)));
        var deadLetter = deadLetterEntry is null ? null : ReadOutbox(deadLetterEntry, deadLetterId!);
        var transition = WorkflowDispatchRedriveTransitions.Evaluate(request, dispatch, deadLetter);
        if (!transition.HasMutation)
            return transition.Result;

        StageUpsert(
            unitOfWork,
            outboxUnit,
            GroundworkV2PostCommitOutboxStorageConventions.Values(transition.OutboxItem!),
            deadLetterEntry!.Version ?? throw new InvalidDataException(
                $"Workflow dispatch dead-letter outbox '{transition.OutboxItem!.OutboxItemId}' did not expose an optimistic revision."));
        StageUpsert(
            unitOfWork,
            dispatchUnit,
            GroundworkV2WorkflowDispatchStorageConventions.Values(transition.WorkflowDispatch!),
            dispatchEntry!.Version ?? throw new InvalidDataException(
                $"Workflow dispatch '{request.DispatchId}' did not expose an optimistic revision."));
        await unitOfWork.CommitAsync(cancellationToken);
        return transition.Result;
    }

    private IReadOnlyList<RuntimePostCommitOutboxItem> QueryCandidates(
        RuntimePostCommitOutboxQuery query,
        CandidateSelection selection,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var session = OpenOutbox();
        var route = SelectRoute(query, selection);
        var table = new TableId(outboxUnit.Name);
        var candidateAt = Column(table, route.CandidateAtField);
        var predicates = new List<Predicate>
        {
            new Predicate.Range(
                candidateAt,
                null,
                Bound.Inclusive(QueryConstant.Of(candidateAt, query.Now)))
        };
        if (selection == CandidateSelection.Claimable)
            predicates.Insert(0, Equal(Column(table, ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableIsEligibleField), true));
        if (query.WorkflowExecutionId is { } workflow)
            predicates.Add(Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField), workflow));
        if (query.IntentKind is { } intentKind)
            predicates.Add(Equal(Column(table, ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField), intentKind));

        var request = new QueryRequest(
            table,
            predicates.Count == 1 ? predicates[0] : new Predicate.And(predicates),
            [.. route.OrderFields.Select(field => new OrderTerm(Column(table, field), OrderDirection.Ascending, NullOrder.Last))],
            Projection.All,
            Paging.Keyset(Math.Min(maximumResults, RuntimeStorePageRequest.MaximumLimit)));
        var options = selection == CandidateSelection.Claimable &&
                      query.WorkflowExecutionId is null &&
                      query.IntentKind is null
            ? outboxUnit.CreateQueryRenderOptions(route.IndexName)
            : null;
        var result = session.Query(request, options);

        var candidates = new List<RuntimePostCommitOutboxItem>(Math.Min(maximumResults, result.Rows.Count));
        foreach (var values in result.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = GroundworkV2PostCommitOutboxStorageConventions.Deserialize(values);
            if (selection == CandidateSelection.Deliverable)
            {
                if (item.Status is RuntimePostCommitOutboxStatus.Pending or RuntimePostCommitOutboxStatus.FailedRetryable)
                    candidates.Add(item);
            }
            else if (RuntimePostCommitOutboxClaimTransitions.CanClaim(
                         item,
                         new RuntimePostCommitOutboxClaimRequest(
                             "query",
                             query.Now,
                             TimeSpan.FromTicks(1),
                             1,
                             query.WorkflowExecutionId,
                             query.IntentKind)))
            {
                candidates.Add(item);
            }
        }

        return candidates.Take(maximumResults).ToArray();
    }

    private (string CandidateAtField, IReadOnlyList<string> OrderFields, string IndexName) SelectRoute(
        RuntimePostCommitOutboxQuery query,
        CandidateSelection selection) =>
        (selection, query.WorkflowExecutionId is not null, query.IntentKind is not null) switch
        {
            (CandidateSelection.Deliverable, false, false) => Route(ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField, "by_deliverable_time_recorded_id"),
            (CandidateSelection.Deliverable, true, false) => Route(ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField, "by_deliverable_by_workflow_time_recorded_id", ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField),
            (CandidateSelection.Deliverable, false, true) => Route(ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField, "by_deliverable_by_intent_kind_time_recorded_id", ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField),
            (CandidateSelection.Deliverable, true, true) => Route(ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField, "by_deliverable_by_workflow_and_intent_kind_time_recorded_id", ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField),
            (CandidateSelection.Claimable, false, false) => ClaimableRoute(ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableIndex),
            (CandidateSelection.Claimable, true, false) => ClaimableRoute(ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableByWorkflowIndex, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField),
            (CandidateSelection.Claimable, false, true) => ClaimableRoute(ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableByIntentKindIndex, ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField),
            (CandidateSelection.Claimable, true, true) => ClaimableRoute(ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableByWorkflowAndIntentKindIndex, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField),
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, null)
        };

    private static (string CandidateAtField, IReadOnlyList<string> OrderFields, string IndexName) ClaimableRoute(
        string indexName,
        params string[] prefix) =>
        Route(ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableAtField, indexName, prefix);

    private static (string CandidateAtField, IReadOnlyList<string> OrderFields, string IndexName) Route(
        string candidateAt,
        string indexName,
        params string[] prefix) =>
        (candidateAt, prefix.Concat([
            candidateAt,
            ElsaRuntimeV2StorageManifest.PostCommitOutboxRecordedAtField,
            ElsaRuntimeV2StorageManifest.PostCommitOutboxItemIdField]).ToArray(), indexName);

    private void StagePendingIfAbsent(
        IUnitOfWork unitOfWork,
        IStorageSession session,
        RuntimePostCommitOutboxItem item)
    {
        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit follow-up items can be staged.");
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(item.OutboxItemId));
        var existing = session.Read(key);
        if (existing is not null)
        {
            if (GroundworkV2PostCommitOutboxStorageConventions.PendingItemsEquivalent(
                    ReadOutbox(existing, item.OutboxItemId),
                    item))
                return;
            throw new InvalidOperationException(
                $"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
        }

        unitOfWork.Stage(RowWrite.Upsert(
            outboxUnit,
            GroundworkV2PostCommitOutboxStorageConventions.Values(item),
            WriteOptions.CreateOnly));
    }

    private static void StageUpsert(
        IUnitOfWork unitOfWork,
        StorageUnit unit,
        StorageValues values,
        long expectedVersion) =>
        unitOfWork.Stage(RowWrite.Upsert(unit, values, WriteOptions.IfVersion(expectedVersion)));

    private IUnitOfWork BeginAtomicUnitOfWork(IReadOnlyList<string> unitIds) => sessions.BeginUnitOfWork(
        Access,
        BatchWriteOptions.Exact,
        unitIds,
        targetName);

    private void RequireAtomicCommit()
    {
        if (sessions is not IGroundworkStorageCapabilitySource capabilitySource ||
            !capabilitySource.Capabilities(targetName).Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException(
                "Groundwork post-commit outbox atomic transitions require the provider's evidenced atomic-commit capability.");
        }
    }

    private IStorageSession OpenOutbox() => sessions.Open(outboxUnit.Id.Value, Access, targetName);

    private StorageAccess Access
    {
        get
        {
            var context = accessContextAccessor.Current ??
                          throw new InvalidOperationException("Groundwork post-commit outbox persistence access context is missing.");
            if (context.Scope is null || context.AcrossScopes)
            {
                throw new InvalidOperationException(
                    "Groundwork post-commit outbox requires one explicit persistence scope; global and across-scope access are refused.");
            }

            return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        }
    }

    private static RuntimePostCommitOutboxItem ReadOutbox(StoredEntry entry, string requestedId)
    {
        var item = GroundworkV2PostCommitOutboxStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(item.OutboxItemId, requestedId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical outbox identity collision detected for '{requestedId}'.");
        }

        return item;
    }

    private static WorkflowDispatchRecord ReadDispatch(StoredEntry entry, string requestedId)
    {
        var record = GroundworkV2WorkflowDispatchStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(record.DispatchId, requestedId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical workflow-dispatch identity collision detected for '{requestedId}'.");
        }
        return record;
    }

    private static WorkflowExecutionState ReadExecution(StoredEntry entry, string requestedId)
    {
        var state = Deserialize<WorkflowExecutionState>(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, requestedId))
            throw new InvalidDataException("Groundwork workflow-execution row identity does not match its key.");
        return state;
    }

    private static T Deserialize<T>(IReadOnlyDictionary<string, object?> values)
    {
        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork runtime row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork runtime row did not contain JSON content.");
        return GroundworkV2RuntimeJson.Deserialize<T>(content)
               ?? throw new InvalidDataException($"Groundwork runtime row could not deserialize as {typeof(T).Name}.");
    }

    private static RuntimePostCommitOutboxItem WithDeliveryState(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxStatus status,
        DateTimeOffset? availableAt,
        int deliveryAttemptCount,
        string? deliveringOwnerId,
        DateTimeOffset? deliveryStartedAt,
        DateTimeOffset? deliveredAt,
        string? lastFailureMessage,
        long deliveryFencingToken,
        DateTimeOffset? deliveryVisibleAfter) =>
        new(
            item.OutboxItemId,
            item.Intent,
            status,
            item.RecordedAt,
            availableAt,
            item.RetryPolicy,
            deliveryAttemptCount,
            deliveringOwnerId,
            deliveryStartedAt,
            deliveredAt,
            lastFailureMessage,
            item.Metadata,
            deliveryFencingToken,
            deliveryVisibleAfter);

    private static RuntimePostCommitOutboxStatus NormalizeDeliveryStatus(
        RuntimePostCommitOutboxItem existing,
        RuntimePostCommitOutboxStatus status,
        int attemptCount) =>
        status == RuntimePostCommitOutboxStatus.FailedRetryable &&
        existing.RetryPolicy.IsExhaustedAfterAttempt(attemptCount)
            ? RuntimePostCommitOutboxStatus.FailedFinal
            : status;

    private static WriteOutcome ConditionalUpsert(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing)
    {
        if (existing.Version is not { } revision)
            throw new InvalidDataException("Groundwork post-commit outbox row did not return an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic post-commit outbox concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;

    private static Predicate Equal(ColumnRef column, string value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate Equal(ColumnRef column, bool value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private ColumnRef Column(TableId table, string name)
    {
        var definition = outboxUnit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork post-commit outbox unit '{outboxUnit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork post-commit outbox query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private enum CandidateSelection
    {
        Deliverable,
        Claimable
    }
}
