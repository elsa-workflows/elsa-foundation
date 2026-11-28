using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.RuntimePostCommitOutboxRow;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Physical Runtime outbox operations shared by prepared delivery and the classic EF adapter.</summary>
internal sealed class EfRuntimePostCommitOutboxStorage(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    TimeProvider timeProvider,
    EfWorkflowExecutableSourceReferenceStore? retentionReferences,
    IWorkflowExecutableRootWriteLeaseManager? retentionLeases,
    bool preparedStandaloneOnly,
    IEfOutboxWorkflowDispatchCompletion? dispatchCompletion = null)
    : IRuntimePostCommitOutboxStore, IPostCommitOutboxLookupStore, IRuntimePostCommitOutboxClaimStore,
      IRuntimePostCommitOutboxClaimCompletionStore, IEfRuntimePostCommitOutboxOwner
{
    private const int ProviderPageSize = RuntimeStorePageRequest.MaximumLimit;

    EfRuntimePostCommitOutboxStorage IEfRuntimePostCommitOutboxOwner.Storage => this;

    internal bool PreparedStandaloneOnly => preparedStandaloneOnly;

    internal bool Uses(RuntimeDbContext expected, PersistenceScope scope) =>
        ReferenceEquals(context, expected) && accessContextAccessor.Current.Scope == scope;

    public bool CanInspectHistorySources(IRuntimeHistoryStore history)
    {
        if (history is not IEfRuntimeHistorySourceOwner reader || !reader.UsesPhysicalHistory(context, accessContextAccessor)) return false;
        try
        {
            _ = accessContextAccessor.Current.RequireScope();
            return context.Database.IsRelational() && RuntimeHistoryEfModule.HasProjectionModel(context);
        }
        catch (InvalidOperationException) { return false; }
    }

    internal bool CanRetainPreparedExecutables(IWorkflowExecutableStore executables, IWorkflowExecutableSourceReferenceStore references) =>
        PreparedStandaloneOnly && CanRetainExecutableFamily(executables, references);

    internal bool CanRetainExecutableFamily(IWorkflowExecutableStore executables, IWorkflowExecutableSourceReferenceStore references,
        Func<PersistenceScope>? legacyScope = null)
    {
        if (executables?.GetMaterialStore() is not EfWorkflowExecutableStore material ||
            references is not EfWorkflowExecutableSourceReferenceStore selectedReferences ||
            retentionReferences is null || retentionLeases is null ||
            !ReferenceEquals(material.Context, context) || !ReferenceEquals(selectedReferences.Context, context)) return false;
        try
        {
            var scope = accessContextAccessor.Current.RequireScope();
            return material.AccessContext.RequireScope() == scope && selectedReferences.AccessContext.RequireScope() == scope &&
                   retentionReferences.AccessContext.RequireScope() == scope &&
                   (legacyScope is null || legacyScope() == scope);
        }
        catch (InvalidOperationException) { return false; }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw new StimulusDeliveryRetentionException(null, exception); }
    }

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

        if (item.RecipientSnapshot?.RetentionAcceptanceVersion is not null)
            throw new InvalidOperationException("A pending source cannot mint verified cohort acceptance.");
        var row = ToEntity(item, scope, id, revision: 1, WriteVersion(item));
        context.RuntimePostCommitOutbox.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
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

    public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCommittedSourceBoundary();
        return FindAsync(outboxItemId, cancellationToken);
    }

    public async ValueTask<RuntimePostCommitOutboxItem?> FindUndeliveredDurableAsync(string workflowExecutionId,
        IReadOnlyCollection<string> excludedIntentKinds, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(excludedIntentKinds);
        var excludedKinds = new HashSet<string>(excludedIntentKinds, StringComparer.Ordinal);
        foreach (var kind in excludedKinds) ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCommittedSourceBoundary();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        try
        {
            var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
            var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
            var executionKey = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
            var executionHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
            string? after = null;
            while (true)
            {
                // Either identity projection can reveal a corrupt owned row. Do not filter on status or
                // kind projections before checking their payload: a damaged projection cannot prove absence.
                var query = context.RuntimePostCommitOutbox.AsNoTracking().Where(row =>
                    (row.ScopeKeyHash == scopeHash || row.ScopeKey == scopeKey) &&
                    (row.WorkflowExecutionIdHash == executionHash || row.WorkflowExecutionId == executionKey));
                if (after is not null) query = query.Where(row => row.Id.CompareTo(after) > 0);
                var page = await query.OrderBy(row => row.Id).Take(ProviderPageSize).ToArrayAsync(cancellationToken);
                foreach (var row in page)
                {
                    var source = ReadChecked(row, scope);
                    if (source.WorkflowExecutionId != workflowExecutionId)
                        throw new InvalidDataException("The durable source differs from its exact execution query.");
                    if (!excludedKinds.Contains(source.DeliveryKind) && source.Status != RuntimePostCommitOutboxStatus.Delivered)
                        return source;
                }
                if (page.Length < ProviderPageSize) return null;
                after = page[^1].Id;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or OperationCanceledException ||
            EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.outbox-state-unavailable",
                $"Prior outbox responsibility could not be read in persistence scope '{scope}' for execution '{workflowExecutionId}'.", exception);
        }
    }

    internal void EnsureCommittedSourceBoundary()
    {
        if (System.Transactions.Transaction.Current is not null || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null)
            throw new InvalidOperationException("A durable source cannot be authorized inside a caller-owned transaction.");
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        PersistenceScope? retentionScope = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCommittedSourceBoundary();
            var scope = accessContextAccessor.Current.RequireScope().Value;
            retentionScope = new(scope);
            var sources = await ReadRetentionSourcesAsync(context, scope, cancellationToken);
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                if (source.SubscriptionDelivery is { } subscriptions)
                {
                    foreach (var recipient in subscriptions.Recipients)
                    {
                        var progress = subscriptions.Progress.Single(item => item.RecipientId == recipient.RecipientId);
                        if (progress.Phase == RuntimeRecipientPhase.CancelledBeforeDispatch) continue;
                        if (source.Status == RuntimePostCommitOutboxStatus.Delivered &&
                            (TransferredToActorPin(source, progress) ||
                             await EfRuntimeExecutionLivenessRow.IsWorkflowExecutionDeletionTombstonedAsync(context, scope, recipient.StartIdentity.WorkflowExecutionId, cancellationToken)))
                            continue;
                        roots.Add(recipient.Material.ArtifactId);
                    }
                    foreach (var wait in subscriptions.WaitDelivery?.Recipients ?? [])
                        if (source.Status != RuntimePostCommitOutboxStatus.Delivered ||
                            !await EfRuntimeExecutionLivenessRow.IsWorkflowExecutionDeletionTombstonedAsync(context, scope, wait.Identity.WorkflowExecutionId, cancellationToken))
                            roots.Add(wait.Expected.RegistrationCapture!.Executable.ArtifactId);
                }
                if (source.RecipientSnapshot is not { } cohort) continue;
                foreach (var start in cohort.Starts)
                {
                    var key = source.RequireWorkflowIntent().IdempotencyKey ?? throw new InvalidDataException("A saved start cohort has no deterministic source key.");
                    var target = KeyedWorkflowStartIdentity.For(key, start.Executable.ArtifactId).WorkflowExecutionId;
                    if (source.Status != RuntimePostCommitOutboxStatus.Delivered ||
                        !await EfRuntimeExecutionLivenessRow.IsWorkflowExecutionDeletionTombstonedAsync(context, scope, target, cancellationToken))
                        roots.Add(start.Executable.ArtifactId);
                }
            }
            EnsureCommittedSourceBoundary();
            return roots.Order(StringComparer.Ordinal).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw new StimulusDeliveryRetentionException(retentionScope, exception); }
    }

    // The protected Actor start commits its executable pin before Foundation can issue this birth receipt.
    // After source delivery settles, only the Actor's retirement proof can release that pin.
    private bool TransferredToActorPin(RuntimePostCommitOutboxItem source, RuntimeRecipientProgress progress) =>
        PreparedStandaloneOnly &&
        source.StandaloneIngress is { } ingress && PreparedStandaloneAdmission.IsMarked(ingress) &&
        progress is { Seal.Version: RuntimeDispatchSeal.PreparedStartQualificationVersion,
            Receipt.Kind: RuntimeTargetAcceptanceKind.PreparedActorBirth, PreparedStartAttempt: { Disarmed: false } attempt } &&
        progress.Receipt.EvidenceId == attempt.Identity.ActorReference;

    // Finite same-scope pages; deliberate conservative traversal on rare GC/deletion paths. Do not use the current
    // claim/publisher authorization read: delivered sources can outlive the publisher and an uncreated target.
    internal static async ValueTask<IReadOnlyList<RuntimePostCommitOutboxItem>> ReadRetentionSourcesAsync(
        RuntimeDbContext context, string scope, CancellationToken cancellationToken)
    {
        var sources = new List<RuntimePostCommitOutboxItem>();
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        string? after = null;
        while (true)
        {
            var query = context.RuntimePostCommitOutbox.AsNoTracking().Where(row =>
                row.ScopeKeyHash == scopeHash || row.ScopeKey == scopeKey);
            // Either scope projection can reveal a corrupt owned row. Page by the actual unique physical
            // key, so contradictory/duplicate logical-order projections cannot hide a source at a page seam.
            if (after is not null) query = query.Where(row => row.Id.CompareTo(after) > 0);
            var page = await query.OrderBy(row => row.Id).Take(ProviderPageSize).ToArrayAsync(cancellationToken);
            foreach (var row in page) sources.Add(ReadChecked(row, scope));
            if (page.Length < ProviderPageSize) return sources;
            after = page[^1].Id;
        }
    }

    internal async ValueTask FenceExecutionDependencyAsync(string scope, string workflowExecutionId, CancellationToken cancellationToken)
    {
        var row = await EfRuntimeExecutionLivenessRow.WorkflowExecutionDeletionOwnershipRows(context, scope, workflowExecutionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (EfRuntimeExecutionLivenessRow.IsWorkflowExecutionDeletionTombstone(row, scope, workflowExecutionId))
            throw new InvalidDataException("A saved cohort execution dependency was retired before acceptance.");
        if (row is null)
        {
            var state = new ExecutionLivenessState(RuntimeExecutionOwnershipStateId.For(workflowExecutionId), workflowExecutionId,
                null, null, null, null, [], new Dictionary<string, string> { [RuntimeMetadataKeys.OwnershipFencingToken] = "0" });
            context.ExecutionLivenessStates.Add(EfRuntimeExecutionLivenessRow.ToEntity(context, state, scope, 1));
            await context.SaveChangesAsync(cancellationToken); // Acquire the unique missing-owner fence before source CAS.
            return;
        }
        _ = RuntimeSchemaWriteVersion.Operational(context, row.SchemaVersion);
        var touched = await context.ExecutionLivenessStates.Where(candidate => candidate.Id == row.Id && candidate.Revision == row.Revision)
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.Revision, row.Revision + 1), cancellationToken);
        if (touched != 1) throw new DbUpdateConcurrencyException("Cohort execution ownership changed before acceptance.");
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
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        return await QueryCandidatesAsync(scope, query, CandidateSelection.Deliverable, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (PreparedStandaloneOnly && (request.WorkflowExecutionId is not null ||
            request.IntentKind != RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind))
            throw new NotSupportedException("This Runtime owner claims only standalone Actor Foundation source deliveries.");
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
        // The prepared owner leaves a mixed page untouched for the preceding source owner.
        if (PreparedStandaloneOnly)
            foreach (var candidate in candidates)
                PreparedStandaloneAdmission.RequireSource(candidate);
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
            if (PreparedStandaloneOnly) PreparedStandaloneAdmission.RequireSource(current);
            if (!RuntimePostCommitOutboxClaimTransitions.CanClaim(current, request))
                continue;

            var claim = RuntimePostCommitOutboxClaimTransitions.Claim(current, request);
            Copy(row, claim.Item, scope, checked(row.Revision + 1), WriteVersion(claim.Item));
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                claims.Add(claim);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Silent by design, and deliberately not what CompleteClaimAsync does with the same exception (#1812).
                // Losing this race means another claimer took the item first: nothing was delivered, nothing is owed,
                // and the next cycle — or the sweep — claims whatever is still claimable. Skipping is the whole
                // correction. The asymmetry is intentional; the reasoning for the other half is at the completion path.
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

    public async ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(
        RuntimePostCommitOutboxClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadAsync(scope, claim.OutboxItemId, tracking: true, cancellationToken);
        if (row is null)
            return null;
        var renewed = RuntimePostCommitOutboxClaimTransitions.Renew(
            ReadChecked(row, scope, claim.OutboxItemId),
            claim,
            now,
            visibilityTimeout);
        if (renewed is null)
            return null;

        Copy(row, renewed.Item, scope, checked(row.Revision + 1), WriteVersion(renewed.Item));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return renewed;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The processor claims a batch and then renews each item on the same scoped context, so the row is still
            // tracked from the claim and the check above ran against that snapshot, not the database. A peer that
            // re-claimed the lapsed item advanced its revision, and the loss surfaces here on the concurrency token. That
            // is the whole point of renewing: report the claim gone and write nothing.
            Detach(row);
            return null;
        }
        catch
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(
        RuntimePostCommitOutboxClaimedQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        const int delivering = (int)RuntimePostCommitOutboxStatus.Delivering;
        const int failedRetryable = (int)RuntimePostCommitOutboxStatus.FailedRetryable;
        const int delivered = (int)RuntimePostCommitOutboxStatus.Delivered;
        // Untracked (Rows), so a poll reads the row as it is now rather than a snapshot this context tracked when it claimed.
        var rows = Rows(scope, query.WorkflowExecutionId, query.IntentKind);
        // The claim deliveries ride in the same statement. A delivered row's claimable time is the maximum, so they come last.
        if (query.DeliveredByClaimSince is { } since)
        {
            var sinceTicks = since.UtcTicks;
            rows = rows.Where(row => row.Status == delivering || row.Status == failedRetryable ||
                                     row.Status == delivered && row.ClaimDeliveredAtUtcTicks >= sinceTicks);
            if (query.ExcludedDeliveredOutboxItemIds.Count > 0)
            {
                // Base64 identity text can compare equal under a provider's collation; the full ordinal projection cannot.
                var excluded = query.ExcludedDeliveredOutboxItemIds.Select(OutboxOrderKey).ToArray();
                rows = rows.Where(row => row.Status != delivered || !excluded.Contains(row.OutboxItemIdOrderKey));
            }
        }
        else
            rows = rows.Where(row => row.Status == delivering || row.Status == failedRetryable);
        return await ReadPageAsync(scope, rows.OrderBy(row => row.ClaimableAtUtcTicks), query.Limit, cancellationToken);
    }

    public async ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadAsync(scope, result.OutboxItemId, tracking: true, cancellationToken)
                   ?? throw NotFound(result.OutboxItemId);
        var current = ReadChecked(row, scope, result.OutboxItemId);
        if (current.SourceKind == RuntimeDeliverySourceKind.StandaloneIngress || current.SubscriptionDeliveryProtocolVersion is not null)
        {
            Detach(row);
            throw new NotSupportedException("Subscription responsibility requires fenced source-aware completion.");
        }
        // Contention is legitimate, not exceptional: a live drain skips the durable claim round-trip while the resumption
        // sweep claims with no execution filter, so the sweep can take this item between this caller's read and this write.
        // Check the retained fence before terminal state: even after a fenced owner completes, a claim-less caller still
        // does not hold that fence. Report the loss and write nothing; a nonterminal item remains a crash backstop that
        // claim expiry and the sweep redeliver idempotently, while an owner-completed item stays terminal.
        if (current.Status == RuntimePostCommitOutboxStatus.Delivering || current.DeliveryFencingToken > 0)
        {
            Detach(row);
            return RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner;
        }
        if (current.IsTerminal)
            throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is already terminal.");

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
        Copy(row, updated, scope, checked(row.Revision + 1), WriteVersion(updated));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row changed under us between the read above and this write — the same contention the status guard
            // catches, only detected by the optimistic-concurrency token instead. It is the identical situation and
            // takes the identical answer: write nothing, report the loss, let the winning deliverer complete the item.
            Detach(row);
            return RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner;
        }
        catch
        {
            Detach(row);
            throw;
        }

        return RuntimePostCommitOutboxClaimCompletionOutcome.Persisted;
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
        Copy(row, completed, scope, checked(row.Revision + 1), WriteVersion(completed));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Same contract, same reasoning as CompleteClaimAsync's concurrency catch below: a presented claim that lost
            // its fence is reported as a stale claim, not swallowed. The re-read raises it; the message covers only the
            // residual case where the claim still appears to own the changed row.
            Detach(row);
            var latest = await LoadAsync(scope, claim.OutboxItemId, tracking: false, cancellationToken);
            if (latest is not null)
            {
                var latestItem = ReadChecked(latest, scope, claim.OutboxItemId);
                _ = RuntimePostCommitOutboxClaimTransitions.Complete(latestItem, claim, result);
            }
            throw new InvalidOperationException(
                $"The claimed post-commit outbox item '{claim.OutboxItemId}' changed concurrently and its claim still " +
                "appears valid; nothing was written, and claim expiry redelivers the item.",
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

        EfOutboxWorkflowDispatchFacts? dispatch = null;
        RuntimePostCommitOutboxEntity? followUpRow = null;
        if (completion.WorkflowDispatch is { } projectedDispatch)
        {
            if (dispatchCompletion is null)
                throw new NotSupportedException("The prepared EF outbox cannot project classic workflow dispatch completion.");
            dispatch = await dispatchCompletion.ReadAsync(scope, projectedDispatch, cancellationToken);
        }

        var resolution = RuntimePostCommitOutboxClaimTransitions.ResolveCompletion(
            completion,
            current,
            dispatch?.Existing,
            dispatch?.Child);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            Copy(row, resolution.OutboxItem, scope, checked(row.Revision + 1), WriteVersion(resolution.OutboxItem));
            if (dispatch is not null && resolution.WorkflowDispatch is { } projected)
                dispatchCompletion!.Stage(scope, dispatch, projected);

            if (resolution.FollowUpOutboxItem is { } followUp)
            {
                followUpRow = await LoadAsync(scope, followUp.OutboxItemId, tracking: true, cancellationToken);
                if (followUpRow is null)
                {
                    followUpRow = ToEntity(followUp, scope, RowId(scope, followUp.OutboxItemId), revision: 1, WriteVersion(followUp));
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
            return resolution.Outcome;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Throws where ClaimAsync above deliberately swallows, and where the claim-less RecordDeliveryResultAsync
            // deliberately reports SupersededByOtherOwner. The three are not the same situation (#1812). A claim-less
            // recording never held a lease, so contention there is inherent; a lost claim costs nothing, because nothing
            // was delivered. A claimant that reaches here held a fenced lease and lost it, which happens only when its
            // own delivery outran the visibility timeout and another deliverer re-claimed the item. That is a broken
            // lease, not routine contention, and IRuntimePostCommitOutboxClaimCompletionStore requires every store to
            // report it as one — the in-memory store and the coalescing overlay answer identically.
            //
            // The re-read exists only to honour that contract at this late detection point. ResolveCompletion above
            // validated the claim against this context's own snapshot, and since one scoped store claims and then
            // completes within a single processor cycle, that snapshot predates the race — so the loss surfaces here on
            // the row's concurrency token instead of there on the fence. Re-running Complete against the current row
            // raises the same RuntimePostCommitOutboxStaleClaimException an early detection raises, so one situation
            // keeps one exception. The InvalidOperationException below is the residue: the row changed but this claim
            // still owns it, or the row is gone — neither of which a fence check can explain.
            //
            // Nothing retries this completion, so the message says what actually recovers. The rollback leaves the item
            // Delivering under the winner's fence and the winner's own completion records the delivery; if the winner
            // also fails, claim expiry returns the item to the sweep, whose redelivery is idempotent. The resumption
            // pump absorbs this throw into its sweep backoff; a coalescing drain propagates it to its caller once the
            // session has deactivated, which is accepted because reaching here at all requires a stalled delivery.
            await RollbackAndDetachAsync(transaction, row, dispatch?.Row, followUpRow);
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
                $"The claimed post-commit outbox item '{completion.Claim.OutboxItemId}' changed concurrently and its " +
                "claim still appears valid; nothing was written, and claim expiry redelivers the item.",
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAndDetachAsync(transaction, row, dispatch?.Row, followUpRow);
            var reconciled = await LoadAsync(scope, completion.Claim.OutboxItemId, tracking: false, cancellationToken);
            if (reconciled is not null && ReadChecked(reconciled, scope, completion.Claim.OutboxItemId).IsEquivalentTo(resolution.OutboxItem))
            {
                var dispatchReconciled = resolution.WorkflowDispatch is not { } settledDispatch ||
                    await dispatchCompletion!.MatchesAsync(scope, settledDispatch, cancellationToken);
                // Check only the follow-up the resolution wrote: child evidence discards the one the completion carried.
                var followUpReconciled = resolution.FollowUpOutboxItem is not { } writtenFollowUp ||
                    await LoadAsync(scope, writtenFollowUp.OutboxItemId, tracking: false, cancellationToken) is { } followUpEntry &&
                    ReadChecked(followUpEntry, scope, writtenFollowUp.OutboxItemId).IsEquivalentTo(writtenFollowUp);
                if (dispatchReconciled && followUpReconciled)
                    return resolution.Outcome;
            }
            throw;
        }
    }

    private async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> QueryCandidatesAsync(
        string scope,
        RuntimePostCommitOutboxQuery query,
        CandidateSelection selection,
        CancellationToken cancellationToken)
    {
        var nowTicks = query.Now.UtcTicks;
        var candidates = Rows(scope, query.WorkflowExecutionId, query.IntentKind);

        if (selection == CandidateSelection.Deliverable)
            candidates = candidates.Where(row => row.DeliverableAtUtcTicks != null && row.DeliverableAtUtcTicks <= nowTicks);
        else
            candidates = candidates.Where(row => row.ClaimableIsEligible && row.ClaimableAtUtcTicks <= nowTicks);

        return await ReadPageAsync(
            scope,
            candidates.OrderBy(row => selection == CandidateSelection.Deliverable ? row.DeliverableAtUtcTicks : row.ClaimableAtUtcTicks),
            query.Limit,
            cancellationToken);
    }

    // The untracked rows of one scope, narrowed to one workflow execution and one intent kind when either is given. Every
    // outbox listing starts here, so the scope and identity predicates are written once.
    private IQueryable<RuntimePostCommitOutboxEntity> Rows(string scope, string? workflowExecutionId, string? intentKind)
    {
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var rows = context.RuntimePostCommitOutbox.AsNoTracking()
            .Where(row => row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash);

        if (workflowExecutionId is not null)
        {
            var workflowKey = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
            var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
            rows = rows.Where(row => row.WorkflowExecutionId == workflowKey && row.WorkflowExecutionIdHash == workflowHash);
        }

        if (intentKind is not null)
        {
            var intentKindHash = EfRuntimeOperationalStoreSupport.Hash(intentKind);
            rows = rows.Where(row => row.IntentKindHash == intentKindHash && row.IntentKind == intentKind);
        }

        return rows;
    }

    // Breaks ties in the listing's own order the same way for every listing, so a page is stable across providers.
    private static async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ReadPageAsync(
        string scope,
        IOrderedQueryable<RuntimePostCommitOutboxEntity> rows,
        int limit,
        CancellationToken cancellationToken)
    {
        var page = await rows
            .ThenBy(row => row.RecordedAtUtcTicks)
            .ThenBy(row => row.OutboxItemIdOrderKey)
            .ThenBy(row => row.Id)
            .Take(Math.Min(limit, ProviderPageSize))
            .ToArrayAsync(cancellationToken);
        return page.Select(row => ReadChecked(row, scope)).ToArray();
    }

    internal async ValueTask<RuntimePostCommitOutboxEntity?> LoadAsync(
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

    internal string WriteVersion(RuntimePostCommitOutboxItem item) => RuntimePostCommitOutboxWriteVersion.For(context, item);

    internal static void ValidatePending(RuntimePostCommitOutboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");
        if (item.SourceKind == RuntimeDeliverySourceKind.StandaloneIngress || item.SubscriptionDelivery is not null || item.SavedStimulusStarts is not null)
            throw new NotSupportedException("A generic pending/checkpoint writer cannot accept standalone sources or captured subscription responsibility.");
        ArgumentException.ThrowIfNullOrWhiteSpace(item.RequireWorkflowIntent().WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.DeliveryKind);
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
            deliveryVisibleAfter,
            item.StimulusDeliveryProtocolVersion,
            item.StimulusDeliverySourceScope,
            item.RecipientSnapshot,
            item.StandaloneIngress,
            item.SubscriptionDeliveryProtocolVersion,
            item.SubscriptionDelivery, item.SavedStimulusStarts);

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

    internal async ValueTask RollbackAndDetachAsync(
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
