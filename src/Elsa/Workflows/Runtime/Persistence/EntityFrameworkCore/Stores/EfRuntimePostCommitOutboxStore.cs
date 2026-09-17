using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core post-commit outbox adapter (R20).</summary>
/// <remarks>
/// Pending persistence, delivery lookup, claims, delivery completion, and the R21 dispatch projection are implemented
/// against the shared relational context. The adapter remains opt-in until the complete runtime checkpoint writer
/// can compose every participant, but completion never hides a partial dispatch write.
/// </remarks>
public sealed class EfRuntimePostCommitOutboxStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IRuntimePostCommitOutboxStore,
    IPostCommitOutboxLookupStore,
    IRuntimePostCommitOutboxClaimStore,
    IRuntimePostCommitOutboxClaimCompletionStore,
    IWorkflowDispatchRedriveStore
{
    private const int ProviderPageSize = RuntimeStorePageRequest.MaximumLimit;

    public async ValueTask SavePendingAsync(
        RuntimePostCommitOutboxItem item,
        CancellationToken cancellationToken = default)
    {
        ValidatePending(item);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = RowId(scope, item.OutboxItemId);
        var existing = await LoadAsync(scope, item.OutboxItemId, tracking: false, cancellationToken);
        if (existing is not null)
        {
            var current = ReadChecked(existing, scope, item.OutboxItemId);
            if (current.IsEquivalentPendingItem(item))
                return;
            throw DifferentPendingItem(item.OutboxItemId);
        }

        var row = ToEntity(item, scope, id, revision: 1);
        context.RuntimePostCommitOutbox.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            Detach(row);
            var winner = await LoadAsync(scope, item.OutboxItemId, tracking: false, cancellationToken);
            if (winner is null)
                throw new InvalidOperationException(
                    $"Post-commit outbox item '{item.OutboxItemId}' conflicted during creation but could not be reloaded.",
                    exception);
            var current = ReadChecked(winner, scope, item.OutboxItemId);
            if (current.IsEquivalentPendingItem(item))
                return;
            throw DifferentPendingItem(item.OutboxItemId, exception);
        }
        catch
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
        string outboxItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadAsync(scope, outboxItemId, tracking: false, cancellationToken);
        return row is null ? null : ReadChecked(row, scope, outboxItemId);
    }

    public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
        RuntimePostCommitOutboxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.OwnerId is not null)
            throw new NotSupportedException("The EF post-commit outbox store does not implement delivery ownership filtering.");

        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        return await QueryCandidatesAsync(scope, query, CandidateSelection.Deliverable, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var candidates = await QueryCandidatesAsync(
            scope,
            new RuntimePostCommitOutboxQuery(
                request.Now,
                request.Limit,
                request.WorkflowExecutionId,
                intentKind: request.IntentKind),
            CandidateSelection.Claimable,
            cancellationToken);
        var claims = new List<RuntimePostCommitOutboxClaim>(Math.Min(request.Limit, candidates.Count));
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (claims.Count == request.Limit)
                break;

            var row = await LoadAsync(scope, candidate.OutboxItemId, tracking: true, cancellationToken);
            if (row is null)
                continue;
            var current = ReadChecked(row, scope, candidate.OutboxItemId);
            if (!RuntimePostCommitOutboxClaimTransitions.CanClaim(current, request))
                continue;

            var claim = RuntimePostCommitOutboxClaimTransitions.Claim(current, request);
            Copy(row, claim.Item, scope, checked(row.Revision + 1));
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                claims.Add(claim);
            }
            catch (DbUpdateConcurrencyException)
            {
                Detach(row);
            }
            catch
            {
                Detach(row);
                throw;
            }
        }

        return claims;
    }

    public async ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadAsync(scope, result.OutboxItemId, tracking: true, cancellationToken)
                   ?? throw NotFound(result.OutboxItemId);
        var current = ReadChecked(row, scope, result.OutboxItemId);
        if (current.IsTerminal)
            throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is already terminal.");
        if (current.Status == RuntimePostCommitOutboxStatus.Delivering || current.DeliveryFencingToken > 0)
        {
            throw new InvalidOperationException(
                $"Post-commit outbox item '{result.OutboxItemId}' is claimed; its owner and fencing token are required.");
        }

        var attemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(current.DeliveryAttemptCount);
        var status = NormalizeDeliveryStatus(current, result.Status, attemptCount);
        var availableAt = status == RuntimePostCommitOutboxStatus.FailedRetryable
            ? result.RecordedAt.Add(current.RetryPolicy.Delay ?? TimeSpan.Zero)
            : (DateTimeOffset?)null;
        var updated = WithDeliveryState(
            current,
            status,
            availableAt,
            attemptCount,
            deliveringOwnerId: null,
            deliveryStartedAt: null,
            deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
            result.FailureMessage,
            current.DeliveryFencingToken,
            deliveryVisibleAfter: null);
        Copy(row, updated, scope, checked(row.Revision + 1));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            Detach(row);
            throw new InvalidOperationException(
                $"The post-commit outbox item '{result.OutboxItemId}' changed concurrently; retry the delivery result.",
                exception);
        }
        catch
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadAsync(scope, claim.OutboxItemId, tracking: true, cancellationToken)
                   ?? throw NotFound(claim.OutboxItemId);
        var current = ReadChecked(row, scope, claim.OutboxItemId);
        var completed = RuntimePostCommitOutboxClaimTransitions.Complete(current, claim, result);
        Copy(row, completed, scope, checked(row.Revision + 1));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            Detach(row);
            var latest = await LoadAsync(scope, claim.OutboxItemId, tracking: false, cancellationToken);
            if (latest is not null)
            {
                var latestItem = ReadChecked(latest, scope, claim.OutboxItemId);
                _ = RuntimePostCommitOutboxClaimTransitions.Complete(latestItem, claim, result);
            }
            throw new InvalidOperationException(
                $"The claimed post-commit outbox item '{claim.OutboxItemId}' changed concurrently; retry the delivery result.",
                exception);
        }
        catch
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadAsync(scope, completion.Claim.OutboxItemId, tracking: true, cancellationToken)
                   ?? throw NotFound(completion.Claim.OutboxItemId);
        var current = ReadChecked(row, scope, completion.Claim.OutboxItemId);
        var completed = RuntimePostCommitOutboxClaimTransitions.Complete(
            current,
            completion.Claim,
            completion.DeliveryResult);

        WorkflowDispatchEntity? dispatchRow = null;
        WorkflowDispatchRecord? winningDispatch = null;
        var admissionWins = false;
        RuntimePostCommitOutboxEntity? followUpRow = null;
        if (completion.WorkflowDispatch is { } projectedDispatch)
        {
            dispatchRow = await context.WorkflowDispatches.SingleOrDefaultAsync(
                candidate => candidate.Id == WorkflowDispatchEfSupport.RowId(scope, projectedDispatch.DispatchId) &&
                             candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                             candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope),
                cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Workflow dispatch '{projectedDispatch.DispatchId}' was not found in the atomic completion context.");
            var existingDispatch = WorkflowDispatchEfSupport.ReadChecked(dispatchRow, scope, projectedDispatch.DispatchId);
            accessContextAccessor.Current.EnsureTenantScope(existingDispatch.TenantId);

            var childId = existingDispatch.ChildWorkflowExecutionId;
            var childRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == EfWorkflowExecutionStateStore.CreateIdForAtomicParticipant(scope, childId) &&
                             candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                             candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope),
                cancellationToken);
            var childExecution = childRow is null
                ? null
                : EfWorkflowExecutionStateStore.ReadChecked(childRow, scope, childId);
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
                Copy(row, completed, scope, checked(row.Revision));
            }
            else
            {
                WorkflowDispatchLifecycle.ValidateTransition(existingDispatch, projectedDispatch);
                winningDispatch = projectedDispatch;
            }

            var selectedDispatch = winningDispatch ?? throw new InvalidOperationException("The dispatch projection did not produce a lifecycle record.");
            if (!admissionWins &&
                (completed.Status != RuntimePostCommitOutboxStatus.FailedFinal ||
                 selectedDispatch.Status != WorkflowDispatchStatus.DispatchFailed))
                throw new InvalidOperationException(
                    "An atomic workflow-dispatch projection is valid only for a final outbox failure and DispatchFailed lifecycle state.");
            if (!completion.Claim.Item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId) ||
                !StringComparer.Ordinal.Equals(dispatchId, selectedDispatch.DispatchId))
                throw new InvalidOperationException(
                    "The workflow-dispatch projection does not match the claimed child-start intent.");

        }

        var outcome = admissionWins
            ? RuntimePostCommitOutboxClaimCompletionOutcome.DeliveredOnChildEvidence
            : RuntimePostCommitOutboxClaimCompletionOutcome.Persisted;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            Copy(row, completed, scope, checked(row.Revision + 1));
            if (dispatchRow is not null && winningDispatch is not null)
                WorkflowDispatchEfSupport.Copy(dispatchRow, winningDispatch, scope, checked(dispatchRow.Revision + 1));

            if (!admissionWins && completion.FollowUpOutboxItem is { } followUp)
            {
                if (StringComparer.Ordinal.Equals(followUp.OutboxItemId, completion.Claim.OutboxItemId))
                    throw new InvalidOperationException("A post-commit follow-up cannot replace the claimed outbox item.");
                followUpRow = await LoadAsync(scope, followUp.OutboxItemId, tracking: true, cancellationToken);
                if (followUpRow is null)
                {
                    followUpRow = ToEntity(followUp, scope, RowId(scope, followUp.OutboxItemId), revision: 1);
                    context.RuntimePostCommitOutbox.Add(followUpRow);
                }
                else
                {
                    var existingFollowUp = ReadChecked(followUpRow, scope, followUp.OutboxItemId);
                    if (!existingFollowUp.IsEquivalentTo(followUp))
                        throw DifferentPendingItem(followUp.OutboxItemId);
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return outcome;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAndDetachAsync(transaction, row, dispatchRow, followUpRow);
            var latest = await LoadAsync(scope, completion.Claim.OutboxItemId, tracking: false, cancellationToken);
            if (latest is not null)
            {
                var latestItem = ReadChecked(latest, scope, completion.Claim.OutboxItemId);
                _ = RuntimePostCommitOutboxClaimTransitions.Complete(
                    latestItem,
                    completion.Claim,
                    completion.DeliveryResult);
            }
            throw new InvalidOperationException(
                $"The claimed post-commit outbox item '{completion.Claim.OutboxItemId}' changed concurrently; retry completion.",
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAndDetachAsync(transaction, row, dispatchRow, followUpRow);
            var reconciled = await LoadAsync(scope, completion.Claim.OutboxItemId, tracking: false, cancellationToken);
            if (reconciled is not null && ReadChecked(reconciled, scope, completion.Claim.OutboxItemId).IsEquivalentTo(completed))
            {
                var dispatchReconciled = true;
                if (winningDispatch is not null)
                {
                    var dispatchEntry = await context.WorkflowDispatches.AsNoTracking().SingleOrDefaultAsync(candidate =>
                        candidate.Id == WorkflowDispatchEfSupport.RowId(scope, winningDispatch.DispatchId), cancellationToken);
                    dispatchReconciled = dispatchEntry is not null &&
                        WorkflowDispatchLifecycle.RecordsEqual(
                            WorkflowDispatchEfSupport.ReadChecked(dispatchEntry, scope, winningDispatch.DispatchId),
                            winningDispatch);
                }
                // Child evidence discards the follow-up, so a completion it won wrote none.
                var followUpReconciled = admissionWins || completion.FollowUpOutboxItem is null ||
                    await LoadAsync(scope, completion.FollowUpOutboxItem.OutboxItemId, tracking: false, cancellationToken) is { } followUpEntry &&
                    ReadChecked(followUpEntry, scope, completion.FollowUpOutboxItem.OutboxItemId).IsEquivalentTo(completion.FollowUpOutboxItem);
                if (dispatchReconciled && followUpReconciled)
                    return outcome;
            }
            throw;
        }
    }

    public async ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
        WorkflowDispatchRedriveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var dispatchRow = await context.WorkflowDispatches.SingleOrDefaultAsync(
            candidate => candidate.Id == WorkflowDispatchEfSupport.RowId(scope, request.DispatchId) &&
                         candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                         candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope), cancellationToken);
        var dispatch = dispatchRow is null ? null : WorkflowDispatchEfSupport.ReadChecked(dispatchRow, scope, request.DispatchId);
        if (dispatch is not null)
            accessContextAccessor.Current.EnsureTenantScope(dispatch.TenantId);
        var deadLetterId = dispatch is null ? null : WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch);
        var deadLetterRow = deadLetterId is null ? null : await LoadAsync(scope, deadLetterId, tracking: true, cancellationToken);
        var deadLetter = deadLetterRow is null ? null : ReadChecked(deadLetterRow, scope, deadLetterId);
        var transition = WorkflowDispatchRedriveTransitions.Evaluate(request, dispatch, deadLetter);
        if (!transition.HasMutation)
            return transition.Result;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            WorkflowDispatchEfSupport.Copy(dispatchRow!, transition.WorkflowDispatch!, scope, checked(dispatchRow!.Revision + 1));
            Copy(deadLetterRow!, transition.OutboxItem!, scope, checked(deadLetterRow!.Revision + 1));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return transition.Result;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAndDetachAsync(transaction, dispatchRow, deadLetterRow);
            throw new InvalidOperationException($"Workflow dispatch '{request.DispatchId}' changed concurrently; retry redrive.", exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAndDetachAsync(transaction, dispatchRow, deadLetterRow);
            throw;
        }
    }

    private async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> QueryCandidatesAsync(
        string scope,
        RuntimePostCommitOutboxQuery query,
        CandidateSelection selection,
        CancellationToken cancellationToken)
    {
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var maximumResults = Math.Min(query.Limit, ProviderPageSize);
        var nowTicks = query.Now.UtcTicks;
        var candidates = context.RuntimePostCommitOutbox.AsNoTracking()
            .Where(row => row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash);

        if (selection == CandidateSelection.Deliverable)
            candidates = candidates.Where(row => row.DeliverableAtUtcTicks != null && row.DeliverableAtUtcTicks <= nowTicks);
        else
            candidates = candidates.Where(row => row.ClaimableIsEligible && row.ClaimableAtUtcTicks <= nowTicks);

        if (query.WorkflowExecutionId is { } workflowExecutionId)
        {
            var workflowKey = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
            var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
            candidates = candidates.Where(row => row.WorkflowExecutionId == workflowKey && row.WorkflowExecutionIdHash == workflowHash);
        }

        if (query.IntentKind is { } intentKind)
        {
            var intentKindHash = EfRuntimeOperationalStoreSupport.Hash(intentKind);
            candidates = candidates.Where(row => row.IntentKindHash == intentKindHash && row.IntentKind == intentKind);
        }

        var rows = await candidates
            .OrderBy(row => selection == CandidateSelection.Deliverable ? row.DeliverableAtUtcTicks : row.ClaimableAtUtcTicks)
            .ThenBy(row => row.RecordedAtUtcTicks)
            .ThenBy(row => row.OutboxItemIdOrderKey)
            .ThenBy(row => row.Id)
            .Take(maximumResults)
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => ReadChecked(row, scope)).ToArray();
    }

    private async ValueTask<RuntimePostCommitOutboxEntity?> LoadAsync(
        string scope,
        string outboxItemId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var id = RowId(scope, outboxItemId);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var query = context.RuntimePostCommitOutbox.Where(row =>
            row.Id == id &&
            row.ScopeKey == scopeKey &&
            row.ScopeKeyHash == scopeHash);
        return tracking
            ? await query.SingleOrDefaultAsync(cancellationToken)
            : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    internal static RuntimePostCommitOutboxEntity ToEntity(
        RuntimePostCommitOutboxItem item,
        string scope,
        string id,
        long revision) => new()
    {
        Id = id,
        ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope),
        ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
        OutboxItemId = EfRuntimeOperationalStoreSupport.Encode(item.OutboxItemId),
        OutboxItemIdHash = EfRuntimeOperationalStoreSupport.Hash(item.OutboxItemId),
        OutboxItemIdOrderKey = OutboxOrderKey(item.OutboxItemId),
        WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(item.Intent.WorkflowExecutionId),
        WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(item.Intent.WorkflowExecutionId),
        WorkflowExecutionIdOrderKey = OrderKey(item.Intent.WorkflowExecutionId),
        IntentKind = item.Intent.Kind,
        IntentKindHash = EfRuntimeOperationalStoreSupport.Hash(item.Intent.Kind),
        Status = (int)item.Status,
        RecordedAtUtcTicks = item.RecordedAt.UtcTicks,
        DeliverableAtUtcTicks = Ticks(DeliverableAt(item)),
        ClaimableAtUtcTicks = ClaimableAt(item).UtcTicks,
        ClaimableIsEligible = IsClaimable(item),
        ContentJson = RuntimeArtifactJson.Serialize(item),
        SchemaVersion = RuntimePostCommitOutboxEfModule.SchemaVersion,
        Revision = revision
    };

    internal static void Copy(
        RuntimePostCommitOutboxEntity row,
        RuntimePostCommitOutboxItem item,
        string scope,
        long revision)
    {
        var replacement = ToEntity(item, scope, row.Id, revision);
        row.ScopeKey = replacement.ScopeKey;
        row.ScopeKeyHash = replacement.ScopeKeyHash;
        row.OutboxItemId = replacement.OutboxItemId;
        row.OutboxItemIdHash = replacement.OutboxItemIdHash;
        row.OutboxItemIdOrderKey = replacement.OutboxItemIdOrderKey;
        row.WorkflowExecutionId = replacement.WorkflowExecutionId;
        row.WorkflowExecutionIdHash = replacement.WorkflowExecutionIdHash;
        row.WorkflowExecutionIdOrderKey = replacement.WorkflowExecutionIdOrderKey;
        row.IntentKind = replacement.IntentKind;
        row.IntentKindHash = replacement.IntentKindHash;
        row.Status = replacement.Status;
        row.RecordedAtUtcTicks = replacement.RecordedAtUtcTicks;
        row.DeliverableAtUtcTicks = replacement.DeliverableAtUtcTicks;
        row.ClaimableAtUtcTicks = replacement.ClaimableAtUtcTicks;
        row.ClaimableIsEligible = replacement.ClaimableIsEligible;
        row.ContentJson = replacement.ContentJson;
        row.SchemaVersion = replacement.SchemaVersion;
        row.Revision = revision;
    }

    internal static RuntimePostCommitOutboxItem ReadChecked(
        RuntimePostCommitOutboxEntity row,
        string scope,
        string? expectedOutboxItemId = null)
    {
        if (row.Revision <= 0 ||
            row.SchemaVersion != RuntimePostCommitOutboxEfModule.SchemaVersion ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
        {
            throw new InvalidDataException("The post-commit outbox row scope, schema, or revision projection is corrupt.");
        }

        string outboxItemId;
        try
        {
            outboxItemId = EfRuntimeOperationalStoreSupport.Decode(row.OutboxItemId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The post-commit outbox logical identity projection is invalid.", exception);
        }

        if (expectedOutboxItemId is not null && !StringComparer.Ordinal.Equals(outboxItemId, expectedOutboxItemId))
        {
            throw new InvalidOperationException(
                $"Post-commit outbox physical identity collision detected for '{expectedOutboxItemId}'.");
        }

        var physicalId = RuntimePostCommitOutboxIdentity.CreateProjectionValue(outboxItemId);
        if (row.Id != RowId(scope, outboxItemId) ||
            row.OutboxItemIdHash != EfRuntimeOperationalStoreSupport.Hash(outboxItemId) ||
            row.OutboxItemIdOrderKey != OutboxOrderKey(outboxItemId))
        {
            throw new InvalidDataException("The post-commit outbox logical identity projection is corrupt.");
        }

        RuntimePostCommitOutboxItem item;
        try
        {
            item = RuntimeArtifactJson.Deserialize<RuntimePostCommitOutboxItem>(row.ContentJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted post-commit outbox item is not valid current data.", exception);
        }

        if (!StringComparer.Ordinal.Equals(item.OutboxItemId, outboxItemId) ||
            row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, physicalId) ||
            row.WorkflowExecutionId != EfRuntimeOperationalStoreSupport.Encode(item.Intent.WorkflowExecutionId) ||
            row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(item.Intent.WorkflowExecutionId) ||
            row.WorkflowExecutionIdOrderKey != OrderKey(item.Intent.WorkflowExecutionId) ||
            row.IntentKind != item.Intent.Kind ||
            row.IntentKindHash != EfRuntimeOperationalStoreSupport.Hash(item.Intent.Kind) ||
            row.Status != (int)item.Status ||
            row.RecordedAtUtcTicks != item.RecordedAt.UtcTicks ||
            row.DeliverableAtUtcTicks != Ticks(DeliverableAt(item)) ||
            row.ClaimableAtUtcTicks != ClaimableAt(item).UtcTicks ||
            row.ClaimableIsEligible != IsClaimable(item))
        {
            throw new InvalidDataException("The post-commit outbox identity or delivery projection does not match its content.");
        }

        return item;
    }

    internal static void ValidatePending(RuntimePostCommitOutboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Intent.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Intent.Kind);
    }

    private static DateTimeOffset? DeliverableAt(RuntimePostCommitOutboxItem item) =>
        item.Status == RuntimePostCommitOutboxStatus.Pending ||
        item.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
        !item.RetryPolicy.IsExhaustedAfterAttempt(item.DeliveryAttemptCount)
            ? item.AvailableAt ?? DateTimeOffset.MinValue
            : null;

    private static DateTimeOffset ClaimableAt(RuntimePostCommitOutboxItem item) =>
        item.Status == RuntimePostCommitOutboxStatus.Delivering
            ? item.DeliveryVisibleAfter ?? DateTimeOffset.MaxValue
            : DeliverableAt(item) ?? DateTimeOffset.MaxValue;

    private static bool IsClaimable(RuntimePostCommitOutboxItem item) =>
        item.Status == RuntimePostCommitOutboxStatus.Delivering
            ? item.DeliveryVisibleAfter is not null
            : DeliverableAt(item) is not null;

    private static long? Ticks(DateTimeOffset? value) => value?.UtcTicks;

    internal static string RowId(string scope, string outboxItemId) =>
        EfRuntimeOperationalStoreSupport.CompositeId(
            scope,
            RuntimePostCommitOutboxIdentity.CreateProjectionValue(outboxItemId));

    internal static string OutboxOrderKey(string value)
        => EfRelationalIdentity.CreateOrdinalTextOrderKey(value);

    private static string OrderKey(string value)
    {
        var physical = RuntimePostCommitOutboxIdentity.CreateProjectionValue(value);
        var prefix = physical[..Math.Min(physical.Length, RuntimePostCommitOutboxEfModule.PhysicalIdentityOrderPrefixMaximumLength)];
        return Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(
            prefix,
            RuntimePostCommitOutboxEfModule.PhysicalIdentityOrderPrefixMaximumLength)) +
            EfRelationalIdentity.Hash(physical);
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

    private static InvalidOperationException DifferentPendingItem(string id, Exception? inner = null) =>
        new($"Post-commit outbox item '{id}' already exists with a different intent or status.", inner);

    private static InvalidOperationException NotFound(string id) =>
        new($"Post-commit outbox item '{id}' was not found.");

    private void Detach(object entity)
    {
        // A failed optimistic write must not leave a modified entity in the scoped context for a later operation.
        // Do not clear the entire tracker: checkpoint participants may have staged sibling rows on this context.
        context.Entry(entity).State = EntityState.Detached;
    }

    private async ValueTask RollbackAndDetachAsync(
        IDbContextTransaction transaction,
        params object?[] entities)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // Preserve the original failure; disposal still closes the provider transaction.
        }

        await transaction.DisposeAsync();
        foreach (var entity in entities.Where(entity => entity is not null))
            context.Entry(entity!).State = EntityState.Detached;
    }

    private enum CandidateSelection
    {
        Deliverable,
        Claimable
    }
}
