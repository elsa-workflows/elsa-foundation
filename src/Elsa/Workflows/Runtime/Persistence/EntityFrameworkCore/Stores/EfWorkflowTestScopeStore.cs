using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF lifecycle and admission store for immutable workflow test scopes (R13).</summary>
public sealed class EfWorkflowTestScopeStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IWorkflowTestScopeStore, IWorkflowTestScopeAdmissionStore
{
    private const string CursorPurpose = "ef-runtime-test-scope-v1";
    private static readonly EfWriteRetry Transitions = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Concurrency);
    private readonly BookmarkStateDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IPersistenceAccessContextAccessor _access = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
    private readonly IRuntimeRecoveryContinuationCodec _codec = continuationCodec ?? throw new ArgumentNullException(nameof(continuationCodec));

    public async ValueTask<WorkflowTestScopeRecord> CreateAsync(WorkflowTestScope scope, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope); cancellationToken.ThrowIfCancellationRequested(); var accessScope = RequireScope(); EnsureAccess(scope); var key = EfRelationalIdentity.Encode(accessScope); var accessScopeHash = EfRelationalIdentity.Hash(accessScope); var encodedScopeId = EfRelationalIdentity.Encode(scope.ScopeId); var scopeIdHash = EfRelationalIdentity.Hash(scope.ScopeId); var id = WorkflowTestScopeEfSupport.Id(accessScope, scope.ScopeId); var existing = await _context.WorkflowTestScopes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.AccessScopeKey == key && x.AccessScopeKeyHash == accessScopeHash && x.ScopeId == encodedScopeId && x.ScopeIdHash == scopeIdHash, cancellationToken); if (existing is not null) return Existing(existing, accessScope, scope);
        var record = WorkflowTestScopeTransitions.Create(null, scope, createdAt); _context.WorkflowTestScopes.Add(WorkflowTestScopeEfSupport.ToEntity(record, accessScope, id));
        try { await _context.SaveChangesAsync(cancellationToken); return record; }
        catch (DbUpdateException) { _context.ChangeTracker.Clear(); var winner = await _context.WorkflowTestScopes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.AccessScopeKey == key && x.AccessScopeKeyHash == accessScopeHash && x.ScopeId == encodedScopeId && x.ScopeIdHash == scopeIdHash, cancellationToken); if (winner is not null) return Existing(winner, accessScope, scope); throw; }
    }

    public async ValueTask<WorkflowTestScopeRecord?> FindAsync(string scopeId, CancellationToken cancellationToken = default)
    {
        ValidateScopeId(scopeId); var accessScope = RequireScope(); var row = await _context.WorkflowTestScopes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == WorkflowTestScopeEfSupport.Id(accessScope, scopeId) && x.AccessScopeKey == EfRelationalIdentity.Encode(accessScope) && x.AccessScopeKeyHash == EfRelationalIdentity.Hash(accessScope) && x.ScopeId == EfRelationalIdentity.Encode(scopeId) && x.ScopeIdHash == EfRelationalIdentity.Hash(scopeId), cancellationToken); return row is null ? null : WorkflowTestScopeEfSupport.Read(row, accessScope, scopeId);
    }

    public async ValueTask<WorkflowTestScopeCloseResult> CloseAsync(WorkflowTestScopeCloseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var accessScope = RequireScope();
        return await Transitions.RunAsync(_context, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await _context.WorkflowTestScopes
                .SingleOrDefaultAsync(x => x.Id == WorkflowTestScopeEfSupport.Id(accessScope, request.ScopeId) && x.AccessScopeKey == EfRelationalIdentity.Encode(accessScope) && x.AccessScopeKeyHash == EfRelationalIdentity.Hash(accessScope) && x.ScopeId == EfRelationalIdentity.Encode(request.ScopeId) && x.ScopeIdHash == EfRelationalIdentity.Hash(request.ScopeId), cancellationToken);
            if (row is null)
                return new WorkflowTestScopeCloseResult(WorkflowTestScopeCloseDisposition.NotFound, null);

            var result = WorkflowTestScopeTransitions.Close(WorkflowTestScopeEfSupport.Read(row, accessScope, request.ScopeId), request);
            if (result.Disposition != WorkflowTestScopeCloseDisposition.Accepted)
                return result;

            WorkflowTestScopeEfSupport.Copy(row, result.Record!, checked(row.Revision + 1));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                throw;
            }
        }, _ => throw new InvalidOperationException($"Workflow test scope '{request.ScopeId}' changed concurrently."), cancellationToken);
    }

    public async ValueTask<WorkflowTestScopeRecord> CompleteAsync(string scopeId, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        ValidateScopeId(scopeId);
        var accessScope = RequireScope();
        return await Transitions.RunAsync(_context, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await _context.WorkflowTestScopes
                .SingleOrDefaultAsync(x => x.Id == WorkflowTestScopeEfSupport.Id(accessScope, scopeId) && x.AccessScopeKey == EfRelationalIdentity.Encode(accessScope) && x.AccessScopeKeyHash == EfRelationalIdentity.Hash(accessScope) && x.ScopeId == EfRelationalIdentity.Encode(scopeId) && x.ScopeIdHash == EfRelationalIdentity.Hash(scopeId), cancellationToken)
                ?? throw new KeyNotFoundException($"Workflow test scope '{scopeId}' was not found.");
            var current = WorkflowTestScopeEfSupport.Read(row, accessScope, scopeId);
            if (current.State == WorkflowTestScopeState.Closed)
                return current;

            var updated = WorkflowTestScopeTransitions.Complete(current, completedAt);

            WorkflowTestScopeEfSupport.Copy(row, updated, checked(row.Revision + 1));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return updated;
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                throw;
            }
        }, _ => throw new InvalidOperationException($"Workflow test scope '{scopeId}' changed concurrently."), cancellationToken);
    }

    public async ValueTask<WorkflowTestScopePage> QueryAsync(WorkflowTestScopePageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidatePageQuery(query);
        if (query.PageSize > RuntimeWorkflowTestScopeEfModule.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(query), $"A workflow test-scope page size cannot exceed {RuntimeWorkflowTestScopeEfModule.MaximumPageSize}.");

        var accessScope = RequireScope();
        var q = _context.WorkflowTestScopes.AsNoTracking()
            .Where(x => x.AccessScopeKey == EfRelationalIdentity.Encode(accessScope) &&
                        x.AccessScopeKeyHash == EfRelationalIdentity.Hash(accessScope));
        if (query.State is { } state)
            q = q.Where(x => x.State == (int)state);
        else
            q = q.Where(x => x.State == (int)WorkflowTestScopeState.Closing ||
                             x.State == (int)WorkflowTestScopeState.Open && x.ExpiresAtUtcTicks <= query.ObservedAt.UtcTicks);

        if (query.ContinuationToken is not null)
        {
            var token = DecodeCursor(query.ContinuationToken);
            if (token.Length != 2 || token[0] != EfRelationalIdentity.Encode(accessScope))
                throw new ArgumentException("The workflow test-scope cursor belongs to another persistence scope.", nameof(query));

            var continuation = DecodeCursorIdentity(token[1], nameof(query));
            q = q.Where(x => x.ScopeIdOrderKey.CompareTo(WorkflowTestScopeEfSupport.Order(continuation)) > 0);
        }

        var rows = await q.OrderBy(x => x.ScopeIdOrderKey)
            .Take(query.PageSize + 1)
            .ToListAsync(cancellationToken);
        var more = rows.Count > query.PageSize;
        if (more)
            rows.RemoveAt(rows.Count - 1);

        return new(
            rows.Select(x => WorkflowTestScopeEfSupport.Read(x, accessScope)).ToArray(),
            more
                ? _codec.Encode(CursorPurpose, Encoding.UTF8.GetBytes($"{EfRelationalIdentity.Encode(accessScope)}\u001f{EfRelationalIdentity.Encode(WorkflowTestScopeEfSupport.Read(rows[^1], accessScope).Scope.ScopeId)}"))
                : null);
    }

    public ValueTask AssertOpenAsync(WorkflowTestScope scope, DateTimeOffset observedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope); cancellationToken.ThrowIfCancellationRequested(); return AssertOpenCore(scope, observedAt, cancellationToken);
    }

    private async ValueTask AssertOpenCore(WorkflowTestScope scope, DateTimeOffset observedAt, CancellationToken ct)
    {
        if (observedAt == default)
            throw new ArgumentOutOfRangeException(nameof(observedAt));

        var accessScope = RequireScope();
        _access.Current.EnsureTenantScope(scope.TenantId);
        var row = await _context.WorkflowTestScopes
            .SingleOrDefaultAsync(x => x.Id == WorkflowTestScopeEfSupport.Id(accessScope, scope.ScopeId) && x.AccessScopeKey == EfRelationalIdentity.Encode(accessScope) && x.AccessScopeKeyHash == EfRelationalIdentity.Hash(accessScope) && x.ScopeId == EfRelationalIdentity.Encode(scope.ScopeId) && x.ScopeIdHash == EfRelationalIdentity.Hash(scope.ScopeId), ct);
        WorkflowTestScopeAdmission.EnsureOpen(
            row is null ? null : WorkflowTestScopeEfSupport.Read(row, accessScope, scope.ScopeId),
            scope,
            observedAt);

        // A revision-only write is the EF equivalent of a same-value
        // conditional upsert. It linearizes admission against a concurrent close:
        // whichever writer reaches the row first invalidates the other's revision. EnsureOpen refused a missing row.
        row!.Revision = checked(row.Revision + 1);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            throw new TestScopeAdmissionException("The workflow test scope changed during admission.");
        }
    }
    private string RequireScope() => _access.Current.RequireScope().Value;
    private void EnsureAccess(WorkflowTestScope scope) => _access.Current.EnsureTenantScope(scope.TenantId);
    private static void ValidateScopeId(string scopeId) { ArgumentException.ThrowIfNullOrWhiteSpace(scopeId); if (scopeId.Length > WorkflowTestScope.MaximumScopeIdLength) throw new ArgumentException($"A workflow test-scope ID must be at most {WorkflowTestScope.MaximumScopeIdLength} UTF-16 code units.", nameof(scopeId)); }
    private string[] DecodeCursor(string cursor)
    {
        try
        {
            return Encoding.UTF8.GetString(_codec.Decode(CursorPurpose, cursor)).Split('\u001f');
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("The workflow test-scope cursor is invalid or does not belong to this query.", nameof(cursor), exception);
        }
    }
    private static string DecodeCursorIdentity(string encoded, string parameterName)
    {
        try
        {
            return EfRelationalIdentity.Decode(encoded);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException)
        {
            throw new ArgumentException("The workflow test-scope cursor contains an invalid identity.", parameterName, exception);
        }
    }
    private static void ValidatePageQuery(WorkflowTestScopePageQuery query)
    {
        if (query.ObservedAt == default)
            throw new ArgumentOutOfRangeException(nameof(query.ObservedAt));
        if (query.PageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.PageSize));
        if (query.State is { } state && !Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(query.State));
        if (query.ContinuationToken is not null && string.IsNullOrWhiteSpace(query.ContinuationToken))
            throw new ArgumentException("The workflow test-scope cursor cannot be blank.", nameof(query.ContinuationToken));
        if (query.ContinuationToken?.Length > 2048)
            throw new ArgumentOutOfRangeException(nameof(query.ContinuationToken));
    }
    private static WorkflowTestScopeRecord Existing(WorkflowTestScopeEntity row, string accessScope, WorkflowTestScope requested) { var existing = WorkflowTestScopeEfSupport.Read(row, accessScope); return WorkflowTestScopeTransitions.Create(existing, requested, existing.CreatedAt); }
}
