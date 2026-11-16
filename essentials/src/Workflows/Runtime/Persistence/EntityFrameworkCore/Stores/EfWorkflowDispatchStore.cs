using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Concrete EF workflow-dispatch lifecycle adapter (R21).</summary>
/// <remarks>
/// Ordinary dispatch lifecycle, bounded queries, cancellation, retention deletion and artifact roots are complete.
/// Fire-and-forget test dispatch admission stages the dispatch and its test-scope revision touch in one EF transaction.
/// The broader checkpoint composition and dispatch completion/redrive surfaces remain owned by their respective slices.
/// </remarks>
public sealed class EfWorkflowDispatchStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowDispatchStore,
    IWorkflowDispatchQueryStore,
    IWorkflowDispatchDeleteStore,
    IWorkflowDispatchRetentionRootStore,
    IWorkflowDispatchAdmissionStore,
    IWorkflowDispatchCancellationStore
{
    private static readonly EfWriteRetry Transitions = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Concurrency);
    private readonly RuntimeDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IPersistenceAccessContextAccessor _access = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));

    public async ValueTask<WorkflowDispatchRecord> SaveAsync(WorkflowDispatchRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        _access.Current.EnsureTenantScope(record.TenantId);
        var id = WorkflowDispatchEfSupport.RowId(scope, record.DispatchId);
        // An insert retries only the unique-key race a concurrent creator wins; an update only its revision race.
        return await Transitions.RunUntilSettledAsync<WorkflowDispatchRecord>(_context, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await LoadAsync(scope, record.DispatchId, tracking: true, cancellationToken);
            if (row is null)
            {
                WorkflowDispatchLifecycle.ValidateNew(record);
                var added = WorkflowDispatchEfSupport.ToEntity(record, scope, id, 1);
                _context.WorkflowDispatches.Add(added);
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    return record;
                }
                catch (Exception exception)
                {
                    Detach(added);
                    if (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
                        return EfWriteAttempt<WorkflowDispatchRecord>.Retry(exception);
                    throw;
                }
            }

            var current = Read(row, scope, record.DispatchId);
            WorkflowDispatchLifecycle.ValidateTransition(current, record);
            if (WorkflowDispatchLifecycle.RecordsEqual(current, record))
                return current;
            WorkflowDispatchEfSupport.Copy(row, record, scope, checked(row.Revision + 1));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return record;
            }
            catch (Exception exception)
            {
                Detach(row);
                if (exception is DbUpdateConcurrencyException)
                    return EfWriteAttempt<WorkflowDispatchRecord>.Retry(exception);
                throw;
            }
        }, _ => throw DidNotSettle(record.DispatchId), cancellationToken);
    }

    public async ValueTask<WorkflowDispatchRecord?> FindAsync(string dispatchId, CancellationToken cancellationToken = default)
    {
        ValidateDispatchId(dispatchId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var row = await LoadAsync(scope, dispatchId, tracking: false, cancellationToken);
        return row is null ? null : Read(row, scope, dispatchId);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(string parentWorkflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        var records = new List<WorkflowDispatchRecord>();
        DateTimeOffset? afterCreatedAt = null;
        string? afterDispatchId = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await QueryAsync(new WorkflowDispatchQuery(
                parentWorkflowExecutionId: parentWorkflowExecutionId,
                take: WorkflowDispatchQuery.MaximumTake,
                afterCreatedAt: afterCreatedAt,
                afterDispatchId: afterDispatchId), cancellationToken);
            records.AddRange(page);
            if (page.Count < WorkflowDispatchQuery.MaximumTake)
                return records;
            var last = page.Last();
            afterCreatedAt = last.CreatedAt;
            afterDispatchId = last.DispatchId;
        }
    }

    public async ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(WorkflowDispatchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeKey = EfRelationalIdentity.Encode(scope);
        var scopeHash = EfRelationalIdentity.Hash(scope);
        var source = _context.WorkflowDispatches.AsNoTracking().Where(x => x.ScopeKey == scopeKey && x.ScopeKeyHash == scopeHash);

        if (query.ParentWorkflowExecutionId is { } parent)
        {
            var hash = EfRelationalIdentity.Hash(parent);
            source = source.Where(x => x.ParentWorkflowExecutionIdHash == hash && x.ParentWorkflowExecutionId == EfRelationalIdentity.Encode(parent));
        }
        if (query.ChildWorkflowExecutionId is { } child)
        {
            var hash = EfRelationalIdentity.Hash(child);
            source = source.Where(x => x.ChildWorkflowExecutionIdHash == hash && x.ChildWorkflowExecutionId == EfRelationalIdentity.Encode(child));
        }
        if (query.Status is { } status)
            source = source.Where(x => x.Status == (int)status);
        if (query.TestScopeId is { } testScope)
        {
            var hash = EfRelationalIdentity.Hash(testScope);
            source = source.Where(x => x.TestScopeIdHash == hash && x.TestScopeId == EfRelationalIdentity.Encode(testScope));
        }
        if (query.AfterCreatedAt is { } afterCreatedAt)
        {
            var orderKey = WorkflowDispatchEfSupport.DispatchOrderKey(query.AfterDispatchId!);
            source = source.Where(x => x.CreatedAtUtcTicks > afterCreatedAt.UtcTicks ||
                                       x.CreatedAtUtcTicks == afterCreatedAt.UtcTicks && string.Compare(x.DispatchIdOrderKey, orderKey) > 0);
        }

        var rows = await source
            .OrderBy(x => x.CreatedAtUtcTicks)
            .ThenBy(x => x.DispatchIdOrderKey)
            .ThenBy(x => x.Id)
            .Take(query.Take)
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => Read(row, scope)).ToArray();
    }

    public async ValueTask<WorkflowDispatchAdmissionResult> TryAdmitAsync(string dispatchId, DateTimeOffset admittedAt, CancellationToken cancellationToken = default)
    {
        ValidateDispatchId(dispatchId);
        if (admittedAt == default)
            throw new ArgumentOutOfRangeException(nameof(admittedAt));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        return await Transitions.RunAsync(_context, async () =>
        {
            var row = await LoadAsync(scope, dispatchId, tracking: true, cancellationToken)
                      ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");
            var current = Read(row, scope, dispatchId);
            if (current.TestScope is not null && current.Mode == WorkflowDispatchMode.FireAndForget)
                return await TryAdmitTestScopedAsync(dispatchId, admittedAt, cancellationToken);
            if (current.Status == WorkflowDispatchStatus.Started)
                return new(WorkflowDispatchAdmissionDisposition.AlreadyAdmitted, current);
            if (WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(current))
                return new(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, current);
            if (current.Status != WorkflowDispatchStatus.Pending)
                return new(WorkflowDispatchAdmissionDisposition.Terminal, current);

            var effectiveAt = admittedAt > current.UpdatedAt ? admittedAt : current.UpdatedAt;
            var candidate = current.TransitionTo(WorkflowDispatchStatus.Started, effectiveAt);
            WorkflowDispatchEfSupport.Copy(row, candidate, scope, checked(row.Revision + 1));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return new WorkflowDispatchAdmissionResult(WorkflowDispatchAdmissionDisposition.Admitted, candidate);
            }
            catch
            {
                Detach(row);
                throw;
            }
        }, _ => throw DidNotSettle(dispatchId), cancellationToken);
    }

    private async ValueTask<WorkflowDispatchAdmissionResult> TryAdmitTestScopedAsync(
        string dispatchId,
        DateTimeOffset admittedAt,
        CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        return await Transitions.RunAsync(_context, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dispatchRow = await LoadAsync(scope, dispatchId, tracking: true, cancellationToken)
                               ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");
            var current = Read(dispatchRow, scope, dispatchId);

            if (current.Status == WorkflowDispatchStatus.Started)
                return new(WorkflowDispatchAdmissionDisposition.AlreadyAdmitted, current);
            if (WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(current))
                return new(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, current);
            if (current.Status != WorkflowDispatchStatus.Pending)
                return new(WorkflowDispatchAdmissionDisposition.Terminal, current);

            var testScope = current.TestScope
                            ?? throw new InvalidDataException($"Workflow dispatch '{dispatchId}' did not contain its test-scope context.");
            var encodedScope = EfRelationalIdentity.Encode(scope);
            var scopeHash = EfRelationalIdentity.Hash(scope);
            var encodedScopeId = EfRelationalIdentity.Encode(testScope.ScopeId);
            var scopeIdHash = EfRelationalIdentity.Hash(testScope.ScopeId);
            var scopeRow = await _context.WorkflowTestScopes.SingleOrDefaultAsync(row =>
                    row.Id == WorkflowTestScopeEfSupport.Id(scope, testScope.ScopeId) &&
                    row.AccessScopeKey == encodedScope &&
                    row.AccessScopeKeyHash == scopeHash &&
                    row.ScopeId == encodedScopeId &&
                    row.ScopeIdHash == scopeIdHash,
                cancellationToken);

            var effectiveAt = admittedAt > current.UpdatedAt ? admittedAt : current.UpdatedAt;
            WorkflowDispatchRecord candidate;
            WorkflowDispatchAdmissionDisposition disposition;
            var admitScope = false;
            if (scopeRow is not null)
            {
                var actualScope = WorkflowTestScopeEfSupport.Read(scopeRow, scope, testScope.ScopeId);
                if (actualScope.State == WorkflowTestScopeState.Open &&
                    !actualScope.Scope.IsExpired(admittedAt) &&
                    WorkflowTestScope.ContextEquals(actualScope.Scope, testScope))
                {
                    candidate = current.TransitionTo(WorkflowDispatchStatus.Started, effectiveAt);
                    disposition = WorkflowDispatchAdmissionDisposition.Admitted;
                    admitScope = true;
                }
                else
                {
                    candidate = WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(current, effectiveAt);
                    disposition = WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission;
                }
            }
            else
            {
                candidate = WorkflowDispatchLifecycle.CancelTestScopeBeforeAdmission(current, effectiveAt);
                disposition = WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission;
            }

            // All validation and projection reads happen before tracked mutation. The two row writes below share
            // one transaction and both carry their original Revision as an EF optimistic-concurrency predicate.
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                WorkflowDispatchEfSupport.Copy(dispatchRow, candidate, scope, checked(dispatchRow.Revision + 1));
                if (admitScope)
                    WorkflowTestScopeEfSupport.StageAdmission(scopeRow!);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new WorkflowDispatchAdmissionResult(disposition, candidate);
            }
            catch
            {
                await RollbackAndDetachAsync(transaction, dispatchRow, scopeRow);
                throw;
            }
        }, _ => throw DidNotSettle(dispatchId), cancellationToken);
    }

    public async ValueTask<WorkflowDispatchCancellationResult> ApplyCancellationAsync(WorkflowDispatchCancellationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        return await Transitions.RunAsync(_context, async () =>
        {
            var row = await LoadAsync(scope, request.DispatchId, tracking: true, cancellationToken);
            var current = row is null ? null : Read(row, scope, request.DispatchId);
            var result = WorkflowDispatchLifecycle.ResolveParentCancellation(current, request);
            if (WorkflowDispatchLifecycle.RecordsEqual(current!, result.Record))
                return result;
            WorkflowDispatchEfSupport.Copy(row!, result.Record, scope, checked(row!.Revision + 1));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch
            {
                Detach(row);
                throw;
            }
        }, _ => throw DidNotSettle(request.DispatchId), cancellationToken);
    }

    public async ValueTask<bool> TryDeleteAsync(WorkflowDispatchRecord expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        _access.Current.EnsureTenantScope(expected.TenantId);
        if (!WorkflowDispatchLifecycle.IsTerminal(expected.Status))
            return false;
        var scope = RequireScope();
        var row = await LoadAsync(scope, expected.DispatchId, tracking: true, cancellationToken);
        if (row is null)
            return true;
        var current = Read(row, scope, expected.DispatchId);
        if (!WorkflowDispatchLifecycle.RecordsEqual(current, expected) || !WorkflowDispatchLifecycle.IsTerminal(current.Status))
            return false;
        _context.WorkflowDispatches.Remove(row);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(row);
            return false;
        }
        catch
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask DeleteAsync(string dispatchId, CancellationToken cancellationToken = default)
    {
        ValidateDispatchId(dispatchId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var row = await LoadAsync(scope, dispatchId, tracking: true, cancellationToken);
        if (row is null)
            return;
        _ = Read(row, scope, dispatchId);
        _context.WorkflowDispatches.Remove(row);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var status in new[] { WorkflowDispatchStatus.Pending, WorkflowDispatchStatus.Started })
        {
            DateTimeOffset? afterCreatedAt = null;
            string? afterDispatchId = null;
            while (true)
            {
                var page = await QueryAsync(new WorkflowDispatchQuery(status: status, take: WorkflowDispatchQuery.MaximumTake, afterCreatedAt: afterCreatedAt, afterDispatchId: afterDispatchId), cancellationToken);
                foreach (var record in page)
                    ids.Add(record.ChildExecutable.ArtifactId);
                if (page.Count < WorkflowDispatchQuery.MaximumTake)
                    break;
                var last = page.Last();
                afterCreatedAt = last.CreatedAt;
                afterDispatchId = last.DispatchId;
            }
        }
        return ids.Order(StringComparer.Ordinal).ToArray();
    }

    private async ValueTask<WorkflowDispatchEntity?> LoadAsync(string scope, string dispatchId, bool tracking, CancellationToken cancellationToken)
    {
        var id = WorkflowDispatchEfSupport.RowId(scope, dispatchId);
        var scopeKey = EfRelationalIdentity.Encode(scope);
        var scopeHash = EfRelationalIdentity.Hash(scope);
        var query = _context.WorkflowDispatches.Where(x => x.Id == id && x.ScopeKey == scopeKey && x.ScopeKeyHash == scopeHash);
        return tracking ? await query.SingleOrDefaultAsync(cancellationToken) : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    private WorkflowDispatchRecord Read(WorkflowDispatchEntity row, string scope, string? expectedDispatchId = null)
    {
        var record = WorkflowDispatchEfSupport.ReadChecked(row, scope, expectedDispatchId);
        _access.Current.EnsureTenantScope(record.TenantId);
        return record;
    }

    private string RequireScope() => EfRuntimeOperationalStoreSupport.RequireScope(_access);

    private static InvalidOperationException DidNotSettle(string dispatchId) =>
        new($"Workflow dispatch '{dispatchId}' changed concurrently and did not settle.");

    private static void ValidateDispatchId(string dispatchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        if (dispatchId.Length > RuntimeWorkflowDispatchEfModule.IdentityMaximumLength)
            throw new ArgumentException($"A workflow dispatch ID cannot exceed {RuntimeWorkflowDispatchEfModule.IdentityMaximumLength} UTF-16 code units.", nameof(dispatchId));
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
            // Preserve the original provider failure or concurrency result.
        }

        foreach (var entity in entities)
        {
            if (entity is not null)
                Detach(entity);
        }
    }

    private void Detach(object entity) => _context.Entry(entity).State = EntityState.Detached;
}
