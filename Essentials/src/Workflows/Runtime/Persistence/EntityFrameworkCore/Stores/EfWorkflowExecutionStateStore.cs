using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Concrete EF adapter for durable workflow execution state (R10).</summary>
public sealed class EfWorkflowExecutionStateStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec,
    TimeProvider? timeProvider = null) : IWorkflowExecutionStateStore, IWorkflowHealthQuery
{
    private const string HistoryCursorPurpose = "ef-runtime-workflow-execution-history-v1";
    private const string HealthCursorPurpose = "ef-runtime-workflow-execution-health-history-v1";
    private const string CaptureCursorPurpose = "ef-runtime-workflow-execution-capture-v1";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<bool> IsDeletionTombstonedAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await EfExecutionLivenessStateStore.IsWorkflowExecutionDeletionTombstonedAsync(
                context, RequireScope(), workflowExecutionId, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            throw Normalize("reading deletion tombstone for", workflowExecutionId, exception);
        }
    }

    public async ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        Validate(state);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        accessContextAccessor.Current.EnsureTenantScope(state.TenantId);
        var id = CreateId(scope, state.WorkflowExecutionId);
        context.ChangeTracker.Clear();
        try
        {
            await using var transaction = context.Database.CurrentTransaction is null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await LockWorkflowExecutionForWriteAsync(context, scope, state.WorkflowExecutionId, cancellationToken);
            await EfExecutionLivenessStateStore.EnsureWorkflowExecutionNotDeletedAsync(context, scope, state.WorkflowExecutionId, cancellationToken);
            var row = await context.WorkflowExecutionStates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null)
                context.WorkflowExecutionStates.Add(ToEntity(state, scope, id, NewRevision()));
            else
            {
                _ = ReadChecked(row, scope, state.WorkflowExecutionId);
                CopyToEntity(row, state, scope, id, checked(row.Revision + 1));
            }

            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return state;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The workflow execution state changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey | EfWriteConflict.Transient))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The workflow execution state changed concurrently; retry the operation.", exception);
        }
        catch (OperationCanceledException) { context.ChangeTracker.Clear(); throw; }
        catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { context.ChangeTracker.Clear(); throw Normalize("saving", state.WorkflowExecutionId, exception); }
    }

    public async ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var scope = RequireScope();
            var id = CreateId(scope, workflowExecutionId);
            var row = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            return row is null ? null : ReadChecked(row, scope, workflowExecutionId);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("reading", workflowExecutionId, exception); }
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new List<WorkflowExecutionState>();
            string? cursor = null;
            do
            {
                var page = await QueryPageAsync(new WorkflowExecutionStatePageQuery(WorkflowExecutionStatePaging.MaximumPageSize, Cursor: cursor), cancellationToken);
                result.AddRange(page.Items);
                cursor = page.NextCursor;
            } while (cursor is not null);
            return result;
        }
        // QueryPageAsync already normalizes its own provider failures; rethrow that type before the
        // classifier below, which would otherwise match the normalized failure too and wrap it again.
        catch (WorkflowExecutionStateEntityFrameworkPersistenceException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("listing", "<all>", exception); }
    }

    public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) =>
        QueryHistoryPageAsync(query, health: null, cancellationToken: cancellationToken);

    public ValueTask<WorkflowExecutionStatePage> QueryHealthPageAsync(
        WorkflowExecutionStatePageQuery query,
        IncidentHealth health,
        CancellationToken cancellationToken = default) =>
        QueryHistoryPageAsync(query, health, cancellationToken);

    public bool SupportsIncidentStore(IIncidentStateStore selectedIncidentStore) =>
        selectedIncidentStore is EfIncidentStateStore incidents &&
        incidents.UsesSameBackingStore(context, accessContextAccessor);

    private async ValueTask<WorkflowExecutionStatePage> QueryHistoryPageAsync(
        WorkflowExecutionStatePageQuery query,
        IncidentHealth? health,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (health is { } requestedHealth)
            ValidateHealth(requestedHealth);
        query.Validate();
        ValidatePageSize(query.PageSize);
        if (query.TenantId is not null)
            ValidateTenant(query.TenantId, nameof(query.TenantId));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var scope = RequireScope();
            var filterScope = health is { } incidentHealth
                ? HealthCursorScope(query, incidentHealth)
                : WorkflowExecutionStateHistory.Scope(query);
            var cursorPurpose = health is null ? HistoryCursorPurpose : HealthCursorPurpose;
            var cursor = DecodeHistoryCursor(query.Cursor, scope, filterScope, cursorPurpose);
            // Decode first so a cursor bound to another filter is reported as
            // cursor misuse; a tenant mismatch without a cursor still fails
            // closed below before any query is executed.
            accessContextAccessor.Current.EnsureTenantScope(query.TenantId);
            var source = HistoryQuery(query, scope);
            if (health is { } requiredHealth)
                source = ApplyIncidentHealth(source, scope, requiredHealth);
            var total = await source.LongCountAsync(cancellationToken);
            if (cursor is not null)
                source = source.Where(x => x.SortTimestampUtcTicks < cursor.SortTicks || x.SortTimestampUtcTicks == cursor.SortTicks && string.Compare(x.WorkflowExecutionIdOrderKey, cursor.OrderKey) > 0);
            var rows = await source.OrderByDescending(x => x.SortTimestampUtcTicks).ThenBy(x => x.WorkflowExecutionIdOrderKey).Take(checked(query.PageSize + 1)).ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > query.PageSize;
            if (hasNext) rows = rows[..query.PageSize];
            var items = rows.Select(x => ReadChecked(x, scope, Decode(x.WorkflowExecutionId))).ToArray();
            return new WorkflowExecutionStatePage(items,
                hasNext && rows.Length > 0 ? EncodeHistoryCursor(rows[^1], scope, filterScope, cursorPurpose) : null,
                hasNext, total);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("querying", "<history>", exception); }
    }

    private IQueryable<WorkflowExecutionStateEntity> ApplyIncidentHealth(
        IQueryable<WorkflowExecutionStateEntity> executions,
        string scope,
        IncidentHealth health)
    {
        var incidents = context.IncidentStates.AsNoTracking()
            .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope));
        var resolved = (int)IncidentStatus.Resolved;
        var suppressed = (int)IncidentStatus.Suppressed;
        var blocking = (int)IncidentStatus.Blocking;

        return health switch
        {
            IncidentHealth.Active => executions.Where(execution => incidents.Any(incident =>
                incident.WorkflowExecutionIdHash == execution.WorkflowExecutionIdHash &&
                incident.WorkflowExecutionId == execution.WorkflowExecutionId &&
                incident.Status != resolved && incident.Status != suppressed)),
            IncidentHealth.Blocking => executions.Where(execution => incidents.Any(incident =>
                incident.WorkflowExecutionIdHash == execution.WorkflowExecutionIdHash &&
                incident.WorkflowExecutionId == execution.WorkflowExecutionId &&
                incident.Status == blocking)),
            IncidentHealth.None => executions.Where(execution => !incidents.Any(incident =>
                incident.WorkflowExecutionIdHash == execution.WorkflowExecutionIdHash &&
                incident.WorkflowExecutionId == execution.WorkflowExecutionId &&
                incident.Status != resolved && incident.Status != suppressed)),
            _ => throw new ArgumentOutOfRangeException(nameof(health), health, "The incident health filter is invalid.")
        };
    }

    private static void ValidateHealth(IncidentHealth health)
    {
        if (health is not (IncidentHealth.Active or IncidentHealth.Blocking or IncidentHealth.None))
            throw new ArgumentOutOfRangeException(nameof(health), health, "The incident health filter is invalid.");
    }

    private static string HealthCursorScope(WorkflowExecutionStatePageQuery query, IncidentHealth health) =>
        $"{WorkflowExecutionStateHistory.Scope(query)}:{(int)health}";

    public async ValueTask<WorkflowExecutionAlterationCapturePage> QueryAlterationCapturePageAsync(WorkflowExecutionAlterationCaptureQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        ValidatePageSize(query.PageSize);
        ValidateTenant(query.TenantPartition, nameof(query.TenantPartition));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var scope = RequireScope();
            if (!StringComparer.Ordinal.Equals(scope, query.TenantPartition))
                throw new InvalidOperationException("The requested resource does not belong to the current persistence scope.");
            var cursor = DecodeCaptureCursor(query.Cursor, query, scope);
            var source = context.WorkflowExecutionStates.AsNoTracking()
                .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) &&
                            x.TenantIdHash == Hash(query.TenantPartition) && x.TenantId == Encode(query.TenantPartition) &&
                            x.AuthorityPartitionKey == query.AuthorityPartitionKey);
            source = ApplySelector(source, query.Selector);
            if (cursor is not null)
                source = source.Where(x => string.Compare(x.WorkflowExecutionIdOrderKey, cursor.OrderKey) > 0);
            var rows = await source.OrderBy(x => x.WorkflowExecutionIdOrderKey).Take(checked(query.PageSize + 1)).ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > query.PageSize;
            if (hasNext) rows = rows[..query.PageSize];
            var items = rows.Select(x =>
            {
                var state = ReadChecked(x, scope, Decode(x.WorkflowExecutionId));
                if (state.Authority is not { } authority || !WorkflowExecutionAuthoritySnapshot.Matches(authority, query.SystemIdentity, query.RootInitiator, query.AuthorityMetadata))
                    throw new InvalidDataException("The persisted workflow execution authority projection does not match its content.");
                return state;
            }).ToArray();
            return new WorkflowExecutionAlterationCapturePage(items,
                hasNext && rows.Length > 0 ? EncodeCaptureCursor(rows[^1], query, scope) : null, hasNext);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("querying", "<alteration-capture>", exception); }
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var scope = RequireScope();
            var rows = await context.WorkflowExecutionStates.AsNoTracking()
                .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope))
                .Select(x => new { x.ArtifactId, x.ArtifactIdHash, x.ArtifactIdOrderKey })
                .Distinct()
                .OrderBy(x => x.ArtifactIdOrderKey)
                .ToArrayAsync(cancellationToken);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ids = new List<string>(rows.Length);
            foreach (var row in rows)
            {
                var id = Decode(row.ArtifactId);
                if (row.ArtifactIdHash != Hash(id) || row.ArtifactIdOrderKey != OrderKey(id)) throw new InvalidDataException("The persisted workflow execution artifact projection is corrupt.");
                if (seen.Add(id)) ids.Add(id);
            }
            return ids.ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("listing pinned artifacts for", "<all>", exception); }
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateId(scope, workflowExecutionId);
        var result = await DeleteCoreAsync(
            workflowExecutionId,
            scope,
            id,
            expectedRevision: null,
            expectedTenantId: null,
            terminalOnly: false,
            removeRunOwnedHistory: false,
            cancellationToken);
        return result == WorkflowExecutionDeletionResult.Deleted;
    }

    internal async ValueTask<WorkflowExecutionDeletionCandidate?> ReadDeletionCandidateAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        try
        {
            var row = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == CreateId(scope, workflowExecutionId) &&
                x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) &&
                x.WorkflowExecutionIdHash == Hash(workflowExecutionId) &&
                x.WorkflowExecutionId == Encode(workflowExecutionId), cancellationToken);
            if (row is null)
                return null;

            var state = ReadChecked(row, scope, workflowExecutionId);
            accessContextAccessor.Current.EnsureTenantScope(state.TenantId);
            return new WorkflowExecutionDeletionCandidate(
                state,
                row.Revision.ToString(CultureInfo.InvariantCulture));
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            throw Normalize("reading deletion candidate for", workflowExecutionId, exception);
        }
    }

    internal async ValueTask<WorkflowExecutionDeletionResult> TryDeleteTerminalAsync(
        WorkflowExecutionDeletionCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidate.State);
        ValidateIdentity(candidate.State.WorkflowExecutionId, nameof(candidate));
        cancellationToken.ThrowIfCancellationRequested();

        accessContextAccessor.Current.EnsureTenantScope(candidate.State.TenantId);
        if (!candidate.State.Status.IsTerminal())
            return WorkflowExecutionDeletionResult.NotTerminal;
        if (!long.TryParse(candidate.ConcurrencyToken, NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
            return WorkflowExecutionDeletionResult.Changed;

        var scope = RequireScope();
        var workflowExecutionId = candidate.State.WorkflowExecutionId;
        return await DeleteCoreAsync(
            workflowExecutionId,
            scope,
            CreateId(scope, workflowExecutionId),
            revision,
            candidate.State.TenantId,
            terminalOnly: true,
            removeRunOwnedHistory: true,
            cancellationToken);
    }

    private async ValueTask<WorkflowExecutionDeletionResult> DeleteCoreAsync(
        string workflowExecutionId,
        string scope,
        string id,
        long? expectedRevision,
        string? expectedTenantId,
        bool terminalOnly,
        bool removeRunOwnedHistory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var ownershipTombstoneInserted = false;
            try
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                // Refuse retained control callers before acquiring this candidate's write lock. Sticky caller
                // tombstones cannot revert; this avoids caller->child / child->caller deletion lock cycles.
                var retiredControlLinks = await ReadRetiredControlLinksAsync(workflowExecutionId, scope, cancellationToken);
                if (retiredControlLinks is null) return WorkflowExecutionDeletionResult.DependencyRetained;
                // A scope-wide header traversal must also precede write locks: two unrelated deletions
                // must not hold their own root while each scans the other's row under READ COMMITTED.
                var hintedChildren = await ReadHintedControlChildrenAsync(workflowExecutionId, scope, cancellationToken);
                // Lock the root before touching any owned rows. Root saves and scheduler enqueue take this same
                // lock, so a deletion cannot purge the queue and then lose the race to a late enqueue.
                await LockWorkflowExecutionForWriteAsync(context, scope, workflowExecutionId, cancellationToken);
                var row = await context.WorkflowExecutionStates.SingleOrDefaultAsync(x =>
                    x.Id == id &&
                    x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) &&
                    x.WorkflowExecutionIdHash == Hash(workflowExecutionId) &&
                    x.WorkflowExecutionId == Encode(workflowExecutionId), cancellationToken);
                if (row is null) return WorkflowExecutionDeletionResult.NotFound;
                var state = ReadChecked(row, scope, workflowExecutionId);
                accessContextAccessor.Current.EnsureTenantScope(state.TenantId);
                if (!StringComparer.Ordinal.Equals(state.TenantId, expectedTenantId) && terminalOnly)
                    return WorkflowExecutionDeletionResult.Changed;
                if (terminalOnly && !state.Status.IsTerminal())
                    return WorkflowExecutionDeletionResult.NotTerminal;
                if (expectedRevision is { } revision && row.Revision != revision)
                    return WorkflowExecutionDeletionResult.Changed;

                // Capture the ownership revision before the final dependency read. An activity-only fenced
                // checkpoint can add its first dispatch without updating the workflow row: either the guard sees
                // it, or final SaveChanges conflicts this captured revision and rolls back the entire deletion.
                ownershipTombstoneInserted = await RevokeExecutionOwnershipAsync(workflowExecutionId, scope, cancellationToken);
                if (await HasControlDependenciesAsync(state, scope, retiredControlLinks, hintedChildren, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    context.ChangeTracker.Clear(); // Never leak the staged tombstone into a later caller SaveChanges.
                    return WorkflowExecutionDeletionResult.DependencyRetained;
                }

                await CancelOutstandingBackgroundActivityWorkAsync(workflowExecutionId, scope, _timeProvider.GetUtcNow(), cancellationToken);

                if (removeRunOwnedHistory)
                    await DeleteRunOwnedHistoryAsync(workflowExecutionId, scope, cancellationToken);

                context.WorkflowExecutionStates.Remove(row);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return WorkflowExecutionDeletionResult.Deleted;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                // A worker claim/result write or a fenced checkpoint changed one of the rows this deletion owns.
                // Retry from fresh projections so the transaction cancels the newly visible claim/receipt too.
                context.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
                return terminalOnly ? WorkflowExecutionDeletionResult.Changed : WorkflowExecutionDeletionResult.NotFound;
            }
            catch (Exception exception) when (
                ownershipTombstoneInserted &&
                EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
            {
                // A first owner raced the tombstone insert. Retry against its persisted lease and revoke it in the
                // same transaction that removes the execution and cancels outstanding background work.
                context.ChangeTracker.Clear();
                if (attempt == 2)
                    throw Normalize("deleting", workflowExecutionId, exception);
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Transient))
            {
                context.ChangeTracker.Clear();
                throw Normalize("deleting", workflowExecutionId, exception);
            }
            catch (OperationCanceledException) { context.ChangeTracker.Clear(); throw; }
            catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
            {
                context.ChangeTracker.Clear();
                throw Normalize("deleting", workflowExecutionId, exception);
            }
        }

        return terminalOnly ? WorkflowExecutionDeletionResult.Changed : WorkflowExecutionDeletionResult.NotFound;
    }

    private async ValueTask<HashSet<string>?> ReadRetiredControlLinksAsync(string workflowExecutionId, string scope,
        CancellationToken cancellationToken)
    {
        var rows = await context.WorkflowDispatches.AsNoTracking().Where(row =>
            row.ScopeKeyHash == Hash(scope) && row.ScopeKey == Encode(scope) &&
            row.ChildWorkflowExecutionIdHash == Hash(workflowExecutionId) && row.ChildWorkflowExecutionId == Encode(workflowExecutionId))
            .ToArrayAsync(cancellationToken);
        var retired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in rows.Select(row => WorkflowDispatchEfSupport.ReadChecked(row, scope)).Where(IsControlLink))
        {
            if (!await EfExecutionLivenessStateStore.IsWorkflowExecutionDeletionTombstonedAsync(
                    context, scope, link.ParentWorkflowExecutionId, cancellationToken)) return null;
            retired.Add(link.DispatchId);
        }
        return retired;
    }

    private static bool IsControlLink(WorkflowDispatchRecord dispatch) =>
        dispatch.Mode == WorkflowDispatchMode.WaitForCompletion &&
        (dispatch.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerKey) ||
         dispatch.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerVersion));

    private async ValueTask<bool> HasControlDependenciesAsync(WorkflowExecutionState state, string scope,
        HashSet<string> retiredControlLinks, IReadOnlyList<(string WorkflowId, string DispatchHint)> hintedChildren,
        CancellationToken cancellationToken)
    {
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var executionHash = Hash(state.WorkflowExecutionId);
        var executionKey = Encode(state.WorkflowExecutionId);
        var rows = await context.WorkflowDispatches.AsNoTracking().Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            (row.ParentWorkflowExecutionIdHash == executionHash && row.ParentWorkflowExecutionId == executionKey ||
             row.ChildWorkflowExecutionIdHash == executionHash && row.ChildWorkflowExecutionId == executionKey))
            .ToArrayAsync(cancellationToken);
        var links = rows.Select(row => WorkflowDispatchEfSupport.ReadChecked(row, scope)).Where(IsControlLink).ToArray();
        var incoming = links.Where(link => link.ChildWorkflowExecutionId == state.WorkflowExecutionId).ToArray();
        if (state.SystemMetadata.TryGetValue(RuntimeMetadataKeys.ChildControlDispatchId, out var hint) &&
            (incoming.Length != 1 || incoming[0].DispatchId != hint || incoming[0].ParentWorkflowExecutionId != state.ParentWorkflowExecutionId))
            return true;
        // Candidate terminal snapshot is revalidated under its write lock. Supported terminal state cannot
        // admit a new called run after the pre-lock header capture; recheck captured links against current authority.
        if (hintedChildren.Any(child => !links.Any(link => link.ParentWorkflowExecutionId == state.WorkflowExecutionId &&
                link.ChildWorkflowExecutionId == child.WorkflowId && link.DispatchId == child.DispatchHint))) return true;
        if (incoming.Any(link => !retiredControlLinks.Contains(link.DispatchId))) return true;
        foreach (var link in links.Where(link => link.ParentWorkflowExecutionId == state.WorkflowExecutionId)
                     .OrderBy(link => link.ChildWorkflowExecutionId, StringComparer.Ordinal))
        {
            if (!state.Status.IsTerminal()) return true;
            // Supported terminal execution is monotonic: its final source/outbox work commits before or with
            // terminal workflow state. Later deliveries resolve existing responsibility; no new producer runs.
            // Read that terminal projection and its owed rows in this transaction; do not lock a second root.
            var child = await ReadLinkedAsync(link.ChildWorkflowExecutionId);
            if (child is null ? (link.Status != WorkflowDispatchStatus.DispatchFailed && !WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(link)) : !child.Status.IsTerminal())
                return true;
            var childHash = Hash(link.ChildWorkflowExecutionId);
            var childKey = Encode(link.ChildWorkflowExecutionId);
            var outboxRows = await context.RuntimePostCommitOutbox.AsNoTracking().Where(row =>
                row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
                (row.WorkflowExecutionIdHash == childHash && row.WorkflowExecutionId == childKey ||
                 row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey))
                .ToArrayAsync(cancellationToken);
            if (outboxRows.Select(row => EfRuntimePostCommitOutboxStore.ReadChecked(row, scope)).Any(item =>
                item.Status != RuntimePostCommitOutboxStatus.Delivered &&
                (item.Intent.WorkflowExecutionId == link.ChildWorkflowExecutionId ||
                 item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var id) && id == link.DispatchId)))
                return true;
        }
        return false;

        async ValueTask<WorkflowExecutionState?> ReadLinkedAsync(string id)
        {
            var row = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == CreateId(scope, id) && candidate.ScopeKeyHash == scopeHash && candidate.ScopeKey == scopeKey &&
                candidate.WorkflowExecutionIdHash == Hash(id) && candidate.WorkflowExecutionId == Encode(id), cancellationToken);
            return row is null ? null : ReadChecked(row, scope, id);
        }
    }

    private async ValueTask<IReadOnlyList<(string WorkflowId, string DispatchHint)>> ReadHintedControlChildrenAsync(
        string callerId, string scope, CancellationToken cancellationToken)
    {
        // Parent identity is retained in the checked workflow payload rather than an indexed projection today.
        // Deletion is rare; traverse finite same-scope pages, retaining on corrupt or missing authority.
        var children = new List<(string WorkflowId, string DispatchHint)>();
        string? after = null;
        while (true)
        {
            var query = context.WorkflowExecutionStates.AsNoTracking().Where(row =>
                row.ScopeKeyHash == Hash(scope) && row.ScopeKey == Encode(scope));
            if (after is not null) query = query.Where(row => row.WorkflowExecutionIdOrderKey.CompareTo(after) > 0);
            var page = await query.OrderBy(row => row.WorkflowExecutionIdOrderKey)
                .Take(RuntimeStorePageRequest.MaximumLimit).ToArrayAsync(cancellationToken);
            foreach (var row in page)
            {
                var child = ReadChecked(row, scope, Decode(row.WorkflowExecutionId));
                if (child.ParentWorkflowExecutionId == callerId &&
                    child.SystemMetadata.TryGetValue(RuntimeMetadataKeys.ChildControlDispatchId, out var hint))
                    children.Add((child.WorkflowExecutionId, hint));
            }
            if (page.Length < RuntimeStorePageRequest.MaximumLimit) return children;
            after = page[^1].WorkflowExecutionIdOrderKey;
        }
    }

    private async ValueTask DeleteRunOwnedHistoryAsync(
        string workflowExecutionId,
        string scope,
        CancellationToken cancellationToken)
    {
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var executionHash = Hash(workflowExecutionId);
        var executionKey = Encode(workflowExecutionId);

        await context.ActivityExecutionStates.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ActivityExecutionInspections.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ActivityExecutionHierarchies.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Bookmarks.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.DurableValueStates.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.SchedulerStates.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.DurableTimers.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.SchedulerWorkItems.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.IncidentStates.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.WorkflowRunHealthStates.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await context.WorkflowSchedulerPoisonRecords.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
        await EfWorkflowHoldStateStore.RemoveWorkflowExecutionHoldsAsync(context, scope, workflowExecutionId, cancellationToken);
        await context.WorkflowHoldStates.Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async ValueTask<bool> RevokeExecutionOwnershipAsync(
        string workflowExecutionId,
        string scope,
        CancellationToken cancellationToken)
    {
        var stateId = RuntimeExecutionOwnershipStateId.For(workflowExecutionId);
        var stateIdHash = EfRuntimeOperationalStoreSupport.Hash(stateId);
        var row = await context.ExecutionLivenessStates.SingleOrDefaultAsync(candidate =>
            candidate.Id == EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, stateId) &&
            candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            candidate.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId) &&
            candidate.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId) &&
            candidate.OperationalStateIdHash == stateIdHash &&
            candidate.OperationalStateId == EfRuntimeOperationalStoreSupport.Encode(stateId), cancellationToken);
        if (row is null)
        {
            // Ownership is normally acquired before the first workflow checkpoint. A legacy or externally seeded
            // execution can predate that invariant, so establish the identity row even when it has never been leased.
            // This insert serializes deletion against a concurrent first-owner insert through the provider's unique key.
            var tombstone = new ExecutionLivenessState(
                stateId,
                workflowExecutionId,
                executionLease: null,
                heartbeat: null,
                drain: null,
                interruptedExecution: null,
                pendingPostCommitIntentIds: [],
                metadata: new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.OwnershipFencingToken] = "0",
                    [RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone] = "true"
                });
            context.ExecutionLivenessStates.Add(EfExecutionLivenessStateStore.ToEntity(tombstone, scope, 1));
            return true;
        }

        var current = EfExecutionLivenessStateStore.Read(row, scope, workflowExecutionId, stateId);
        // Preserve the monotonic token and unrelated operational state. Clearing the lease makes every checkpoint
        // carrying the deleted execution's fence fail, while the row revision makes an in-flight fenced commit
        // conflict with this transaction.
        var metadata = new Dictionary<string, string>(current.Metadata, StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone] = "true"
        };
        var revoked = new ExecutionLivenessState(
            stateId,
            workflowExecutionId,
            executionLease: null,
            heartbeat: null,
            current.Drain,
            current.InterruptedExecution,
            current.PendingPostCommitIntentIds,
            metadata);
        EfExecutionLivenessStateStore.Copy(row, revoked, scope, checked(row.Revision + 1));
        return false;
    }

    private async ValueTask CancelOutstandingBackgroundActivityWorkAsync(
        string workflowExecutionId,
        string scope,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
        var pending = (int)BackgroundActivityWorkStatus.Pending;
        var claimed = (int)BackgroundActivityWorkStatus.Claimed;
        var resultReady = (int)BackgroundActivityWorkStatus.ResultReady;
        var rows = await context.BackgroundActivityWorks.Where(candidate =>
            candidate.ScopeKeyHash == scopeHash && candidate.ScopeKey == scopeKey &&
            candidate.WorkflowExecutionIdHash == workflowHash && candidate.WorkflowExecutionId == workflowExecutionId &&
            (candidate.Status == pending || candidate.Status == claimed || candidate.Status == resultReady))
            .ToArrayAsync(cancellationToken);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = EfBackgroundActivityWorkStore.ReadChecked(row, scope, workflowExecutionId, row.WorkId);
            var cancelled = BackgroundActivityWorkLifecycle.Cancel(item, occurredAt);
            if (!ReferenceEquals(item, cancelled))
                EfBackgroundActivityWorkStore.Copy(row, cancelled, scope);
        }
    }

    private IQueryable<WorkflowExecutionStateEntity> HistoryQuery(WorkflowExecutionStatePageQuery query, string scope)
    {
        var source = context.WorkflowExecutionStates.AsNoTracking().Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope));
        if (query.TenantId is not null) source = source.Where(x => x.TenantIdHash == Hash(query.TenantId) && x.TenantId == Encode(query.TenantId));
        if (query.DefinitionId is not null) source = source.Where(x => x.DefinitionIdHash == Hash(query.DefinitionId) && x.DefinitionId == Encode(query.DefinitionId));
        if (query.Status is { } status) source = source.Where(x => x.Status == (int)status);
        if (query.RunKind is { } runKind) source = source.Where(x => x.RunKind == (int)runKind);
        if (query.CorrelationId is not null) source = source.Where(x => x.CorrelationIdHash == Hash(query.CorrelationId) && x.CorrelationId == Encode(query.CorrelationId));
        if (query.WorkflowExecutionId is not null) source = source.Where(x => x.WorkflowExecutionIdHash == Hash(query.WorkflowExecutionId) && x.WorkflowExecutionId == Encode(query.WorkflowExecutionId));
        if (query.ArtifactId is not null) source = source.Where(x => x.ArtifactIdHash == Hash(query.ArtifactId) && x.ArtifactId == Encode(query.ArtifactId));
        if (query.From is { } from) source = source.Where(x => x.SortTimestampUtcTicks >= from.UtcTicks);
        if (query.To is { } to) source = source.Where(x => x.SortTimestampUtcTicks <= to.UtcTicks);
        return source;
    }

    private static IQueryable<WorkflowExecutionStateEntity> ApplySelector(IQueryable<WorkflowExecutionStateEntity> source, WorkflowAlterationQuerySelector selector)
    {
        if (selector.DefinitionId is not null) source = source.Where(x => x.DefinitionIdHash == Hash(selector.DefinitionId) && x.DefinitionId == Encode(selector.DefinitionId));
        if (selector.Status is { } status) source = source.Where(x => x.Status == (int)status);
        if (selector.RunKind is { } runKind) source = source.Where(x => x.RunKind == (int)runKind);
        if (selector.CorrelationId is not null) source = source.Where(x => x.CorrelationIdHash == Hash(selector.CorrelationId) && x.CorrelationId == Encode(selector.CorrelationId));
        if (selector.WorkflowExecutionId is not null) source = source.Where(x => x.WorkflowExecutionIdHash == Hash(selector.WorkflowExecutionId) && x.WorkflowExecutionId == Encode(selector.WorkflowExecutionId));
        if (selector.ArtifactId is not null) source = source.Where(x => x.ArtifactIdHash == Hash(selector.ArtifactId) && x.ArtifactId == Encode(selector.ArtifactId));
        if (selector.From is { } from) source = source.Where(x => x.SortTimestampUtcTicks >= from.UtcTicks);
        if (selector.To is { } to) source = source.Where(x => x.SortTimestampUtcTicks <= to.UtcTicks);
        return source;
    }

    // Internal checkpoint staging reuses the same projection as the direct store. Keeping this seam internal
    // prevents the atomic writer from growing a second, subtly different workflow-execution envelope.
    internal static WorkflowExecutionStateEntity ToEntity(WorkflowExecutionState state, string scope, string id, long revision)
    {
        var row = new WorkflowExecutionStateEntity();
        CopyToEntity(row, state, scope, id, revision);
        return row;
    }

    internal static void CopyToEntity(WorkflowExecutionStateEntity row, WorkflowExecutionState state, string scope, string id, long revision)
    {
        var timestamp = WorkflowExecutionStateHistory.SortTimestamp(state);
        var tenant = state.TenantId;
        var correlation = state.CorrelationId;
        var definition = state.PinnedSource?.DefinitionId ?? state.PinnedExecutable.DefinitionId;
        row.Id = id; row.ScopeKey = Encode(scope); row.ScopeKeyHash = Hash(scope);
        row.WorkflowExecutionId = Encode(state.WorkflowExecutionId); row.WorkflowExecutionIdHash = Hash(state.WorkflowExecutionId); row.WorkflowExecutionIdOrderKey = OrderKey(state.WorkflowExecutionId);
        row.TenantId = tenant is null ? null : Encode(tenant); row.TenantIdHash = tenant is null ? null : Hash(tenant);
        row.DefinitionId = Encode(definition); row.DefinitionIdHash = Hash(definition); row.Status = (int)state.Status; row.RunKind = (int)state.RunKind; row.SortTimestampUtcTicks = timestamp.UtcTicks;
        row.CorrelationId = correlation is null ? null : Encode(correlation); row.CorrelationIdHash = correlation is null ? null : Hash(correlation);
        row.ArtifactId = Encode(state.PinnedExecutable.ArtifactId); row.ArtifactIdHash = Hash(state.PinnedExecutable.ArtifactId); row.ArtifactIdOrderKey = OrderKey(state.PinnedExecutable.ArtifactId);
        row.AuthorityPartitionKey = state.Authority is { } authority ? WorkflowExecutionAuthoritySnapshot.PartitionKey(authority.SystemIdentity, authority.RootInitiator, authority.Metadata) : null;
        row.ContentJson = RuntimeArtifactJson.Serialize(state); row.SchemaVersion = RuntimeWorkflowExecutionEfModule.SchemaVersion; row.Revision = revision;
    }

    internal static WorkflowExecutionState ReadChecked(WorkflowExecutionStateEntity row, string scope, string expectedId)
    {
        try
        {
            return ReadCheckedCore(row, scope, expectedId);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted workflow execution state is not valid current data.", exception);
        }
    }

    internal static string CreateIdForAtomicParticipant(string scope, string id) => CreateId(scope, id);

    private static WorkflowExecutionState ReadCheckedCore(WorkflowExecutionStateEntity row, string scope, string expectedId)
    {
        if (EfSchemaVersion.NotReadable(RuntimeWorkflowExecutionEfModule.Chain, row.SchemaVersion) || row.Id != CreateId(scope, expectedId) || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) || row.Revision <= 0)
            throw new InvalidDataException("The persisted workflow execution state envelope is corrupt.");
        WorkflowExecutionState state;
        try
        {
            state = RuntimeArtifactJson.Deserialize<WorkflowExecutionState>(RuntimeWorkflowExecutionEfModule.Chain.Upcast<WorkflowExecutionStateEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted workflow execution state content is not valid current JSON.", exception);
        }
        Validate(state);
        if (state.TenantId is not null && !StringComparer.Ordinal.Equals(state.TenantId, scope))
            throw new InvalidDataException("The persisted workflow execution tenant does not match its persistence scope.");
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, expectedId) || !StringComparer.Ordinal.Equals(row.WorkflowExecutionId, Encode(expectedId)) || row.WorkflowExecutionIdHash != Hash(expectedId) || row.WorkflowExecutionIdOrderKey != OrderKey(expectedId))
            throw new InvalidDataException("The persisted workflow execution identity projection does not match its content.");
        var definition = state.PinnedSource?.DefinitionId ?? state.PinnedExecutable.DefinitionId;
        if (row.DefinitionId != Encode(definition) || row.DefinitionIdHash != Hash(definition) || row.Status != (int)state.Status || row.RunKind != (int)state.RunKind || row.SortTimestampUtcTicks != WorkflowExecutionStateHistory.SortTimestamp(state).UtcTicks)
            throw new InvalidDataException("The persisted workflow execution history projection does not match its content.");
        EnsureOptional(row.TenantId, row.TenantIdHash, state.TenantId); EnsureOptional(row.CorrelationId, row.CorrelationIdHash, state.CorrelationId);
        if (row.ArtifactId != Encode(state.PinnedExecutable.ArtifactId) || row.ArtifactIdHash != Hash(state.PinnedExecutable.ArtifactId) || row.ArtifactIdOrderKey != OrderKey(state.PinnedExecutable.ArtifactId))
            throw new InvalidDataException("The persisted workflow execution artifact projection does not match its content.");
        var authorityKey = state.Authority is { } authority ? WorkflowExecutionAuthoritySnapshot.PartitionKey(authority.SystemIdentity, authority.RootInitiator, authority.Metadata) : null;
        if (row.AuthorityPartitionKey != authorityKey)
            throw new InvalidDataException("The persisted workflow execution authority projection does not match its content.");
        return state;
    }

    /// <summary>
    /// Takes the per-execution write lock used to serialize root saves, deletion, and late scheduler enqueue.
    /// Keeping this update revision-neutral preserves snapshot tokens while the surrounding transaction holds the lock.
    /// </summary>
    internal static async ValueTask<int> LockWorkflowExecutionForWriteAsync(
        RuntimeDbContext context,
        string scope,
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var id = CreateId(scope, workflowExecutionId);
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var executionHash = Hash(workflowExecutionId);
        var executionKey = Encode(workflowExecutionId);
        return await context.WorkflowExecutionStates
            .Where(row => row.Id == id &&
                          row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
                          row.WorkflowExecutionIdHash == executionHash && row.WorkflowExecutionId == executionKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, row => row.Revision), cancellationToken);
    }

    private static void EnsureOptional(string? encoded, string? hash, string? value)
    {
        if ((encoded is null) != (value is null) || (hash is null) != (value is null) || value is not null && (encoded != Encode(value) || hash != Hash(value)))
            throw new InvalidDataException("The persisted workflow execution optional projection is corrupt.");
    }

    private static void Validate(WorkflowExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state); ValidateIdentity(state.WorkflowExecutionId, nameof(state.WorkflowExecutionId)); ArgumentNullException.ThrowIfNull(state.PinnedExecutable);
        ValidateIdentity(state.PinnedExecutable.ArtifactId, nameof(state.PinnedExecutable.ArtifactId)); ValidateIdentity(state.PinnedExecutable.DefinitionId, nameof(state.PinnedExecutable.DefinitionId));
        if (state.PinnedSource is { } source) ValidateIdentity(source.DefinitionId, nameof(source.DefinitionId));
        if (state.TenantId is not null) ValidateTenant(state.TenantId, nameof(state.TenantId)); if (state.CorrelationId is not null) ValidateIdentity(state.CorrelationId, nameof(state.CorrelationId));
        if (!Enum.IsDefined(state.Status) || !Enum.IsDefined(state.RunKind)) throw new InvalidDataException("The persisted workflow execution state contains an undefined enum value.");
    }

    private string RequireScope()
    {
        var scope = accessContextAccessor.Current.RequireScope().Value;
        ValidateTenant(scope, nameof(scope));
        return scope;
    }

    private static void ValidateIdentity(string value, string name) { ArgumentException.ThrowIfNullOrWhiteSpace(value); if (value.Length > RuntimeWorkflowExecutionEfModule.IdentityMaximumLength) throw new ArgumentException($"The {name} value cannot exceed {RuntimeWorkflowExecutionEfModule.IdentityMaximumLength} characters.", name); }
    private static void ValidateTenant(string value, string name) { ArgumentException.ThrowIfNullOrWhiteSpace(value); if (value.Length > RuntimeWorkflowExecutionEfModule.TenantMaximumLength) throw new ArgumentException($"The {name} value cannot exceed {RuntimeWorkflowExecutionEfModule.TenantMaximumLength} characters.", name); }
    private static void ValidatePageSize(int pageSize) { if (pageSize > WorkflowExecutionStatePaging.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"Page size cannot exceed {WorkflowExecutionStatePaging.MaximumPageSize} rows."); }
    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string Decode(string value)
    {
        try { return EfRelationalIdentity.Decode(value); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The persisted workflow execution identity projection is not valid.", exception);
        }
    }
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string OrderKey(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeWorkflowExecutionEfModule.IdentityMaximumLength));
    // Length framing makes the composite identity unambiguous; EfRelationalIdentity hashes the
    // UTF-16 code units directly so distinct opaque values (including lone surrogates) remain distinct.
    // The checkpoint staging seam must use the same framed identity as direct workflow-execution persistence.
    internal static string CreateId(string scope, string id) => Hash($"workflow-execution:{scope.Length}:{scope}{id.Length}:{id}");
    private static long NewRevision() { Span<byte> bytes = stackalloc byte[8]; RandomNumberGenerator.Fill(bytes); var result = BitConverter.ToInt64(bytes) & (long.MaxValue >> 1); return result == 0 ? 1 : result; }
    private static WorkflowExecutionStateEntityFrameworkPersistenceException Normalize(string action, string id, Exception exception) =>
        new(action, id, $"EF workflow execution state {action} failed for '{id}'.", exception);

    private HistoryCursor? DecodeHistoryCursor(string? token, string scope, string filterScope, string purpose)
    {
        if (token is null) return null;
        try { var cursor = RuntimeArtifactJson.Deserialize<HistoryCursor>(Encoding.UTF8.GetString(continuationCodec.Decode(purpose, token))); if (cursor.Version != 1 || cursor.ScopeHash != Hash(scope) || cursor.FilterScope != filterScope || cursor.OrderKey != OrderKey(cursor.ExecutionId)) throw new FormatException(); return cursor; }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException) { throw new ArgumentException("The workflow execution history cursor is invalid or does not belong to this query.", nameof(token), ex); }
    }
    private string EncodeHistoryCursor(WorkflowExecutionStateEntity row, string scope, string filterScope, string purpose) => continuationCodec.Encode(purpose, Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new HistoryCursor(1, Hash(scope), filterScope, Decode(row.WorkflowExecutionId), row.SortTimestampUtcTicks, row.WorkflowExecutionIdOrderKey))));
    private CaptureCursor? DecodeCaptureCursor(string? token, WorkflowExecutionAlterationCaptureQuery query, string scope)
    {
        if (token is null) return null;
        try { var cursor = RuntimeArtifactJson.Deserialize<CaptureCursor>(Encoding.UTF8.GetString(continuationCodec.Decode(CaptureCursorPurpose, token))); if (cursor.Version != 1 || cursor.ScopeHash != Hash(scope) || cursor.FilterScope != CaptureScope(query) || cursor.OrderKey != OrderKey(cursor.ExecutionId)) throw new FormatException(); return cursor; }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException) { throw new ArgumentException("The alteration capture cursor is invalid or does not belong to this query.", nameof(token), ex); }
    }
    private string EncodeCaptureCursor(WorkflowExecutionStateEntity row, WorkflowExecutionAlterationCaptureQuery query, string scope) => continuationCodec.Encode(CaptureCursorPurpose, Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new CaptureCursor(1, Hash(scope), CaptureScope(query), Decode(row.WorkflowExecutionId), row.WorkflowExecutionIdOrderKey))));
    private static string CaptureScope(WorkflowExecutionAlterationCaptureQuery query) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new { query.TenantPartition, query.SystemIdentity, query.RootInitiator, query.AuthorityPartitionKey, Metadata = query.AuthorityMetadata.OrderBy(x => x.Key, StringComparer.Ordinal), query.Selector.DefinitionId, query.Selector.Status, query.Selector.RunKind, From = query.Selector.From?.UtcTicks, To = query.Selector.To?.UtcTicks, query.Selector.CorrelationId, query.Selector.WorkflowExecutionId, query.Selector.ArtifactId, query.Selector.MatchAllAuthorized }))));
    private sealed record HistoryCursor(int Version, string ScopeHash, string FilterScope, string ExecutionId, long SortTicks, string OrderKey);
    private sealed record CaptureCursor(int Version, string ScopeHash, string FilterScope, string ExecutionId, string OrderKey);
}

internal static class WorkflowExecutionStatePaging
{
    public const int MaximumPageSize = 500;
}
