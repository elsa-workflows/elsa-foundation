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
/// Test-scope admission is intentionally fail-closed until the test-scope participant can share this context without
/// clearing sibling checkpoint state; the concrete adapter is therefore an opt-in preview, not a default replacement.
/// </remarks>
public sealed class EfWorkflowDispatchStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowDispatchStore,
    IWorkflowDispatchQueryStore,
    IWorkflowDispatchDeleteStore,
    IWorkflowDispatchRetentionRootStore,
    IWorkflowDispatchAdmissionStore,
    IWorkflowDispatchCancellationStore
{
    private const int MaxTransitionAttempts = 16;
    private readonly BookmarkStateDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IPersistenceAccessContextAccessor _access = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));

    public async ValueTask<WorkflowDispatchRecord> SaveAsync(WorkflowDispatchRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        _access.Current.EnsureTenantScope(record.TenantId);
        var id = WorkflowDispatchEfSupport.RowId(scope, record.DispatchId);
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
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
                catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
                {
                    Detach(added);
                    continue;
                }
                catch
                {
                    Detach(added);
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

        throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' changed concurrently and did not settle.");
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
            var orderKey = WorkflowDispatchEfSupport.OrderKey(query.AfterDispatchId!);
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
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var row = await LoadAsync(scope, dispatchId, tracking: true, cancellationToken)
                      ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for child admission.");
            var current = Read(row, scope, dispatchId);
            if (current.TestScope is not null && current.Mode == WorkflowDispatchMode.FireAndForget)
                throw new NotSupportedException("EF test-scoped dispatch admission remains unavailable until it can atomically update the test-scope participant.");
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
                return new(WorkflowDispatchAdmissionDisposition.Admitted, candidate);
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
        throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' changed concurrently and did not settle.");
    }

    public async ValueTask<WorkflowDispatchCancellationResult> ApplyCancellationAsync(WorkflowDispatchCancellationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var row = await LoadAsync(scope, request.DispatchId, tracking: true, cancellationToken)
                      ?? throw new InvalidOperationException($"Workflow dispatch '{request.DispatchId}' was not found for parent cancellation.");
            var current = Read(row, scope, request.DispatchId);
            ValidateCancellationIdentity(current, request);
            if (WorkflowDispatchLifecycle.IsTerminal(current.Status))
                return new(WorkflowDispatchCancellationDisposition.TerminalUnchanged, current);

            var candidate = current.Status == WorkflowDispatchStatus.Pending
                ? WorkflowDispatchLifecycle.CancelBeforeAdmission(current, request.RequestedAt)
                : WorkflowDispatchLifecycle.MarkCancellationRequested(current, request.RequestedAt);
            var disposition = current.Status == WorkflowDispatchStatus.Pending
                ? WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission
                : WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission;
            if (WorkflowDispatchLifecycle.RecordsEqual(current, candidate))
                return new(disposition, current);
            WorkflowDispatchEfSupport.Copy(row, candidate, scope, checked(row.Revision + 1));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return new(disposition, candidate);
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
        throw new InvalidOperationException($"Workflow dispatch '{request.DispatchId}' changed concurrently and did not settle.");
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
        await _context.SaveChangesAsync(cancellationToken);
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

    private static void ValidateDispatchId(string dispatchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        if (dispatchId.Length > RuntimeWorkflowDispatchEfModule.IdentityMaximumLength)
            throw new ArgumentException($"A workflow dispatch ID cannot exceed {RuntimeWorkflowDispatchEfModule.IdentityMaximumLength} UTF-16 code units.", nameof(dispatchId));
    }

    private static void ValidateCancellationIdentity(WorkflowDispatchRecord record, WorkflowDispatchCancellationRequest request)
    {
        if (!StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, request.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ParentActivityExecutionId, request.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, request.ChildWorkflowExecutionId))
            throw new InvalidOperationException($"Workflow dispatch cancellation identity does not match '{request.DispatchId}'.");
    }

    private void Detach(object entity) => _context.Entry(entity).State = EntityState.Detached;
}
