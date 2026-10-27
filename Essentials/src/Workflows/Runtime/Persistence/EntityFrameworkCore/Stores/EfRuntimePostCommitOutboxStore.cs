using System.Text.Json;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Executions;
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
public sealed class EfRuntimePostCommitOutboxStore : IRuntimePostCommitOutboxStore,
    IPostCommitOutboxLookupStore,
    IRuntimePostCommitOutboxClaimStore,
    IRuntimePostCommitOutboxClaimCompletionStore,
    IWorkflowDispatchRedriveStore,
    IStimulusDeliveryStore
{
    private readonly RuntimeDbContext context;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly TimeProvider timeProvider;
    private readonly WorkflowExecutableStartSourceResolver? retentionSources;
    private readonly IWorkflowExecutableRootWriteLeaseManager? retentionLeases;
    private readonly EfWorkflowExecutableSourceReferenceStore? retentionReferences;

    public EfRuntimePostCommitOutboxStore(RuntimeDbContext context, IPersistenceAccessContextAccessor accessContextAccessor)
        : this(context, accessContextAccessor, TimeProvider.System) { }

    public EfRuntimePostCommitOutboxStore(RuntimeDbContext context, IPersistenceAccessContextAccessor accessContextAccessor,
        TimeProvider? timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.accessContextAccessor = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public EfRuntimePostCommitOutboxStore(RuntimeDbContext context, IPersistenceAccessContextAccessor accessContextAccessor,
        TimeProvider? timeProvider, IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager)
        : this(context, accessContextAccessor, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sourceReferenceStore); ArgumentNullException.ThrowIfNull(rootWriteLeaseManager);
        if (sourceReferenceStore is EfWorkflowExecutableSourceReferenceStore references && ReferenceEquals(references.Context, context))
        {
            retentionReferences = references;
            retentionSources = new(references.WithAccess(accessContextAccessor), this.timeProvider);
            retentionLeases = rootWriteLeaseManager;
        }
    }

    private const int ProviderPageSize = RuntimeStorePageRequest.MaximumLimit;

    async ValueTask IStimulusDeliveryStore.EnsureOrdinaryCollectionAllowedAsync(IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore executables,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox); cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(outbox, this) || executables.GetMaterialStore() is not EfWorkflowExecutableStore material ||
            references is not EfWorkflowExecutableSourceReferenceStore selectedReferences || executions is not EfWorkflowExecutionStateStore states ||
            !ReferenceEquals(material.Context, context) || !ReferenceEquals(selectedReferences.Context, context) || !ReferenceEquals(states.Context, context))
            throw new NotSupportedException("Ordinary collection requires its actual EF material/outbox/reference/execution context.");
        PersistenceScope? scope = null;
        try
        {
            EnsureCommittedSourceBoundary();
            if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Ordinary collection requires a clean context.");
            scope = accessContextAccessor.Current.RequireScope();
            if (material.AccessContext.RequireScope() != scope || selectedReferences.AccessContext.RequireScope() != scope || states.AccessContext.RequireScope() != scope)
                throw new NotSupportedException("Ordinary collection requires one actual EF material/outbox/reference/execution scope.");
            var sources = await ReadRetentionSourcesAsync(context, scope.Value, cancellationToken);
            if (sources.Any(source => source.StimulusDeliveryProtocolVersion is not null || source.StimulusDeliverySourceScope is not null || source.RecipientSnapshot is not null))
                throw new StimulusDeliveryRetentionException(scope, new InvalidOperationException("Ordinary collection cannot serve saved stimulus protocol data."));
            EnsureCommittedSourceBoundary();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (StimulusDeliveryRetentionException) { throw; }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw new StimulusDeliveryRetentionException(scope, exception); }
    }

    bool IStimulusDeliveryStore.CanRetainExecutables(IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore executables,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions)
    {
        if (!ReferenceEquals(outbox, this) || executables?.GetMaterialStore() is not EfWorkflowExecutableStore material ||
            references is not EfWorkflowExecutableSourceReferenceStore selectedReferences ||
            executions is not EfWorkflowExecutionStateStore states ||
            retentionReferences is null || retentionLeases is null ||
            !ReferenceEquals(material.Context, context) || !ReferenceEquals(selectedReferences.Context, context) ||
            !ReferenceEquals(states.Context, context)) return false;
        try
        {
            var scope = accessContextAccessor.Current.RequireScope();
            return material.AccessContext.RequireScope() == scope && selectedReferences.AccessContext.RequireScope() == scope &&
                   retentionReferences.AccessContext.RequireScope() == scope &&
                   states.AccessContext.RequireScope() == scope;
        }
        catch (InvalidOperationException) { return false; }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw new StimulusDeliveryRetentionException(null, exception); }
    }

    async ValueTask<IReadOnlyCollection<string>> IStimulusDeliveryStore.ListPinnedExecutableArtifactIdsAsync(
        IWorkflowExecutableStore executables, IWorkflowExecutableSourceReferenceStore references,
        IWorkflowExecutionStateStore executions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executables); ArgumentNullException.ThrowIfNull(references); ArgumentNullException.ThrowIfNull(executions);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!((IStimulusDeliveryStore)this).CanRetainExecutables(this, executables, references, executions))
                throw new NotSupportedException("Physical stimulus retention requires the actual EF stores, context and scope.");
            if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Physical stimulus retention requires a clean context.");
            return await ListPinnedExecutableArtifactIdsAsync(cancellationToken);
        }
        catch (StimulusDeliveryRetentionException exception) when (cancellationToken.IsCancellationRequested && exception.InnerException is OperationCanceledException)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw;
        }
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

    private void EnsureCommittedSourceBoundary()
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
                if (source.RecipientSnapshot is not { } cohort) continue;
                foreach (var start in cohort.Starts)
                {
                    var key = source.Intent.IdempotencyKey ?? throw new InvalidDataException("A saved start cohort has no deterministic source key.");
                    var target = KeyedWorkflowStartIdentity.For(key, start.Executable.ArtifactId).WorkflowExecutionId;
                    if (source.Status != RuntimePostCommitOutboxStatus.Delivered ||
                        !await EfExecutionLivenessStateStore.IsWorkflowExecutionDeletionTombstonedAsync(context, scope, target, cancellationToken))
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

    public async ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.MatchesSource(source)) throw new ArgumentException("The proposed cohort does not match its source.", nameof(candidate));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var access = accessContextAccessor.Current; var scope = access.RequireScope(); access.EnsureScope(source.Scope);
            EnsureCommittedSourceBoundary();
            if (retentionSources is null || retentionLeases is null)
                throw new NotSupportedException("Verified cohort installation requires coherent native reference and root-lease stores.");
            if (context.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Verified cohort installation requires a clean context for its owned transaction.");
            Exception? lastConflict = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                context.ChangeTracker.Clear();
                try
                {
                    var initial = await ReadFacts();
                    _ = initial.Source.InstallRecipientSnapshot(source, candidate, initial.Publisher, scope, timeProvider.GetUtcNow());
                    if (initial.Source.RecipientSnapshot is { } existing) return Verified(existing);
                    if (candidate.SelectedAt < source.Intent.RecordedAt || candidate.SelectedAt > timeProvider.GetUtcNow())
                        throw new InvalidDataException("The recipient selection timestamp is outside this source delivery.");
                    var dependencies = candidate.GetExecutionDependencies(source.Intent);
                    var roots = candidate.Starts.Select(start => start.Executable).Append(initial.Publisher.PinnedExecutable).ToArray();
                    StimulusDeliverySnapshot? result = null;
                    await retentionLeases.ExecuteFencedAsync(roots, "stimulus:" + source.SourceOutboxItemId + ":" + Guid.NewGuid().ToString("N"), async (grants, token) =>
                    {
                        await retentionSources.ValidateCapturedAsync(candidate.Starts, token);
                        await using var transaction = await context.Database.BeginTransactionAsync(token);
                        try
                        {
                            // Material is locked before ownership. All workflow/reference facts are read before
                            // ownership locks; deletion may hold another workflow root while scanning source rows.
                            await new EfWorkflowExecutableStore(context, accessContextAccessor).FenceRootWriteLeasesAsync(grants, timeProvider, token);
                            var facts = await ReadFacts(token);
                            if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(facts.Publisher.PinnedExecutable, initial.Publisher.PinnedExecutable))
                                throw new InvalidDataException("The publisher executable changed before cohort acceptance.");
                            _ = facts.Source.InstallRecipientSnapshot(source, candidate, facts.Publisher, scope, timeProvider.GetUtcNow());
                            if (facts.Source.RecipientSnapshot is { } winner) { result = Verified(winner); return; }
                            foreach (var id in dependencies) await FenceExecutionDependencyAsync(scope.Value, id, token);
                            // Re-read physical source/claim after all awaited fences. No workflow-root read occurs here.
                            var row = await LoadAsync(scope.Value, source.SourceOutboxItemId, tracking: true, token) ??
                                throw new InvalidDataException("The exact recipient source is missing.");
                            await context.Entry(row).ReloadAsync(token);
                            if (context.Entry(row).State == EntityState.Detached) throw new InvalidDataException("The recipient source was removed.");
                            var current = ReadChecked(row, scope.Value, source.SourceOutboxItemId);
                            var accepted = new StimulusDeliverySnapshot(candidate.Version, candidate.SourceOutboxItemId,
                                candidate.IntentFingerprint, candidate.Scope, candidate.SelectedAt, candidate.StimulusType,
                                candidate.StimulusHash, candidate.Starts, candidate.Resumes, StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);
                            var installed = current.InstallRecipientSnapshot(source, accepted, facts.Publisher, scope, timeProvider.GetUtcNow());
                            if (current.RecipientSnapshot is { } laterWinner) { result = Verified(laterWinner); return; }
                            token.ThrowIfCancellationRequested();
                            Copy(row, installed, scope.Value, checked(row.Revision + 1), WriteVersion(installed));
                            await context.SaveChangesAsync(token);
                            await transaction.CommitAsync(token);
                            result = Verified(installed.RecipientSnapshot!);
                        }
                        catch
                        {
                            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                            throw;
                        }
                    }, cancellationToken);
                    context.ChangeTracker.Clear();
                    return result ?? throw new InvalidOperationException("Verified cohort installation did not produce a persisted result.");
                }
                catch (Exception exception) when (exception is DbUpdateConcurrencyException ||
                    EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
                {
                    lastConflict = exception; context.ChangeTracker.Clear();
                }
            }
            throw new IOException("The recipient source remained contended; retry without dispatching.", lastConflict);

            async ValueTask<(RuntimePostCommitOutboxItem Source, WorkflowExecutionState Publisher)> ReadFacts(CancellationToken token = default)
            {
                if (token == default) token = cancellationToken;
                var row = await LoadAsync(scope.Value, source.SourceOutboxItemId, tracking: false, token) ??
                    throw new InvalidDataException("The exact recipient source is missing.");
                var publisherRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.Id == EfWorkflowExecutionStateStore.CreateId(scope.Value, source.Intent.WorkflowExecutionId), token) ??
                    throw new InvalidDataException("The authoritative publisher is missing.");
                var publisher = EfWorkflowExecutionStateStore.ReadChecked(publisherRow, scope.Value, source.Intent.WorkflowExecutionId);
                access.EnsureTenantScope(publisher.TenantId);
                row = await LoadAsync(scope.Value, source.SourceOutboxItemId, tracking: false, token) ??
                    throw new InvalidDataException("The recipient source was removed during publisher validation.");
                return (ReadChecked(row, scope.Value, source.SourceOutboxItemId), publisher);
            }
            static StimulusDeliverySnapshot Verified(StimulusDeliverySnapshot snapshot) =>
                snapshot.RetentionAcceptanceVersion == StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion ? snapshot :
                    throw new InvalidDataException("The existing data-only cohort has no verified retention acceptance; it must not be restamped.");
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { context.ChangeTracker.Clear(); throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            throw new StimulusDeliveryBoundaryException(source.SourceOutboxItemId, exception,
                exception is WorkflowExecutableRootWriteLeaseLostException or WorkflowExecutableRootWriteLeaseUnavailableException ||
                EfRelationalExceptionClassifier.IsProviderFailure(exception) ||
                exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or WorkflowExecutableReferenceRejectedException));
        }
    }

    private async ValueTask FenceExecutionDependencyAsync(string scope, string workflowExecutionId, CancellationToken cancellationToken)
    {
        var row = await EfExecutionLivenessStateStore.WorkflowExecutionDeletionOwnershipRows(context, scope, workflowExecutionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (EfExecutionLivenessStateStore.IsWorkflowExecutionDeletionTombstone(row, scope, workflowExecutionId))
            throw new InvalidDataException("A saved cohort execution dependency was retired before acceptance.");
        if (row is null)
        {
            var state = new ExecutionLivenessState(RuntimeExecutionOwnershipStateId.For(workflowExecutionId), workflowExecutionId,
                null, null, null, null, [], new Dictionary<string, string> { [RuntimeMetadataKeys.OwnershipFencingToken] = "0" });
            context.ExecutionLivenessStates.Add(EfExecutionLivenessStateStore.ToEntity(state, scope, 1));
            await context.SaveChangesAsync(cancellationToken); // Acquire the unique missing-owner fence before source CAS.
            return;
        }
        var touched = await context.ExecutionLivenessStates.Where(candidate => candidate.Id == row.Id && candidate.Revision == row.Revision)
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.Revision, row.Revision + 1), cancellationToken);
        if (touched != 1) throw new DbUpdateConcurrencyException("Cohort execution ownership changed before acceptance.");
    }

    public async ValueTask<PostCommitDeliveryContext> ReadSavedStartSourceAsync(WorkflowExecutableStimulusDeliveryAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority); cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var access = accessContextAccessor.Current; var scope = access.RequireScope(); access.EnsureScope(authority.Scope);
            EnsureCommittedSourceBoundary();
            var row = await LoadAsync(scope.Value, authority.SourceOutboxItemId, tracking: false, cancellationToken)
                ?? throw new InvalidDataException("The exact saved-start source is missing.");
            var current = ReadChecked(row, scope.Value, authority.SourceOutboxItemId);
            var publisherRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == EfWorkflowExecutionStateStore.CreateId(scope.Value, current.Intent.WorkflowExecutionId), cancellationToken)
                ?? throw new InvalidDataException("The authoritative saved-start publisher is missing.");
            var publisher = EfWorkflowExecutionStateStore.ReadChecked(publisherRow, scope.Value, current.Intent.WorkflowExecutionId);
            access.EnsureTenantScope(publisher.TenantId);
            // Publisher lookup awaited a separate read. Do not authorize the earlier claim snapshot if a peer
            // completed/reclaimed the source meanwhile, even when its old visibility deadline is still future.
            row = await LoadAsync(scope.Value, authority.SourceOutboxItemId, tracking: false, cancellationToken)
                ?? throw new InvalidDataException("The saved-start source was removed during publisher validation.");
            current = ReadChecked(row, scope.Value, authority.SourceOutboxItemId);
            EnsureCommittedSourceBoundary();
            return current.OpenSavedStartSource(authority, publisher, scope, timeProvider.GetUtcNow());
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        { throw new StimulusDeliveryBoundaryException(authority.SourceOutboxItemId, exception,
            EfRelationalExceptionClassifier.IsProviderFailure(exception) ||
            exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)); }
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
        // Untracked (Rows), so a poll reads the row as it is now rather than a snapshot this context tracked when it claimed.
        return await ReadPageAsync(
            scope,
            Rows(scope, query.WorkflowExecutionId, query.IntentKind)
                .Where(row => row.Status == delivering || row.Status == failedRetryable)
                .OrderBy(row => row.ClaimableAtUtcTicks),
            query.Limit,
            cancellationToken);
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

        WorkflowDispatchEntity? dispatchRow = null;
        WorkflowDispatchRecord? existingDispatch = null;
        WorkflowExecutionState? childExecution = null;
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
            existingDispatch = WorkflowDispatchEfSupport.ReadChecked(dispatchRow, scope, projectedDispatch.DispatchId);
            accessContextAccessor.Current.EnsureTenantScope(existingDispatch.TenantId);

            var childId = existingDispatch.ChildWorkflowExecutionId;
            var childRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == EfWorkflowExecutionStateStore.CreateIdForAtomicParticipant(scope, childId) &&
                             candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                             candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope),
                cancellationToken);
            childExecution = childRow is null
                ? null
                : EfWorkflowExecutionStateStore.ReadChecked(childRow, scope, childId);
        }

        var resolution = RuntimePostCommitOutboxClaimTransitions.ResolveCompletion(
            completion,
            current,
            existingDispatch,
            childExecution);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            Copy(row, resolution.OutboxItem, scope, checked(row.Revision + 1), WriteVersion(resolution.OutboxItem));
            if (dispatchRow is not null && resolution.WorkflowDispatch is not null)
                WorkflowDispatchEfSupport.Copy(dispatchRow, resolution.WorkflowDispatch, scope, checked(dispatchRow.Revision + 1));

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
                $"The claimed post-commit outbox item '{completion.Claim.OutboxItemId}' changed concurrently and its " +
                "claim still appears valid; nothing was written, and claim expiry redelivers the item.",
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAndDetachAsync(transaction, row, dispatchRow, followUpRow);
            var reconciled = await LoadAsync(scope, completion.Claim.OutboxItemId, tracking: false, cancellationToken);
            if (reconciled is not null && ReadChecked(reconciled, scope, completion.Claim.OutboxItemId).IsEquivalentTo(resolution.OutboxItem))
            {
                var dispatchReconciled = true;
                if (resolution.WorkflowDispatch is { } dispatch)
                {
                    var dispatchEntry = await context.WorkflowDispatches.AsNoTracking().SingleOrDefaultAsync(candidate =>
                        candidate.Id == WorkflowDispatchEfSupport.RowId(scope, dispatch.DispatchId), cancellationToken);
                    dispatchReconciled = dispatchEntry is not null &&
                        WorkflowDispatchLifecycle.RecordsEqual(
                            WorkflowDispatchEfSupport.ReadChecked(dispatchEntry, scope, dispatch.DispatchId),
                            dispatch);
                }
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
            Copy(deadLetterRow!, transition.OutboxItem!, scope, checked(deadLetterRow!.Revision + 1), WriteVersion(transition.OutboxItem!));
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

    private string WriteVersion(RuntimePostCommitOutboxItem item) => RuntimePostCommitOutboxWriteVersion.For(context, item);

    internal static RuntimePostCommitOutboxEntity ToEntity(
        RuntimePostCommitOutboxItem item,
        string scope,
        string id,
        long revision,
        string writeVersion)
    {
        if (item.StimulusDeliverySourceScope is { } sourceScope && sourceScope.Value != scope)
            throw new InvalidOperationException("The declared recipient source does not belong to this provider write scope.");
        return new()
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
            SchemaVersion = writeVersion,
            Revision = revision
        };
    }

    internal static void Copy(
        RuntimePostCommitOutboxEntity row,
        RuntimePostCommitOutboxItem item,
        string scope,
        long revision,
        string writeVersion)
    {
        var replacement = ToEntity(item, scope, row.Id, revision, writeVersion);
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
        if (EfSchemaVersion.NotReadable(RuntimePostCommitOutboxEfModule.Chain, row.SchemaVersion) ||
            row.Revision <= 0 ||
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
            using var content = JsonDocument.Parse(row.ContentJson);
            foreach (var name in new[] { "recipientSnapshot", "stimulusDeliveryProtocolVersion", "stimulusDeliverySourceScope" })
            {
                var fields = content.RootElement.EnumerateObject()
                    .Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (fields.Length > 1 || fields.Any(property => property.Value.ValueKind == JsonValueKind.Null))
                    throw new InvalidDataException("Present recipient protocol state cannot be null or duplicated.");
            }
            item = RuntimeArtifactJson.Deserialize<RuntimePostCommitOutboxItem>(RuntimePostCommitOutboxEfModule.Chain.Upcast<RuntimePostCommitOutboxEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted post-commit outbox item is not valid current data.", exception);
        }

        if (item.StimulusDeliverySourceScope is { } sourceScope && sourceScope.Value != scope)
            throw new InvalidDataException("The recipient protocol scope differs from its physical source row.");

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
            deliveryVisibleAfter,
            item.StimulusDeliveryProtocolVersion,
            item.StimulusDeliverySourceScope,
            item.RecipientSnapshot);

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
