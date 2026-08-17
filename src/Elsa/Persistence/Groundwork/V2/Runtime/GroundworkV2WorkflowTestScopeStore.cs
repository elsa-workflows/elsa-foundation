using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 workflow test-scope lifecycle and admission store.</summary>
public sealed class GroundworkV2WorkflowTestScopeStore : IWorkflowTestScopeStore, IWorkflowTestScopeAdmissionStore
{
    internal const int MaximumPageSize = 100;
    private const int MaxTransitionAttempts = 16;

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2WorkflowTestScopeStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind, targetName);
    }

    public ValueTask<WorkflowTestScopeRecord> CreateAsync(
        WorkflowTestScope scope,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTenant(scope.TenantId);
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowTestScopeStorageConventions.PhysicalId(scope.ScopeId));
        var existing = session.Read(key);
        if (existing is not null)
        {
            var record = Read(existing, scope.ScopeId);
            EnsureTenant(record.Scope.TenantId);
            return ValueTask.FromResult(WorkflowTestScopeTransitions.Create(record, scope, createdAt));
        }

        var candidate = WorkflowTestScopeTransitions.Create(null, scope, createdAt);
        var result = session.Insert(GroundworkV2WorkflowTestScopeStorageConventions.Values(candidate), WriteOptions.CreateOnly);
        if (IsSaved(result.Status))
            return ValueTask.FromResult(candidate);
        if (result.Status == WriteOutcomeStatus.ConcurrencyConflict)
        {
            var winner = session.Read(key) ?? throw new InvalidOperationException(
                $"Workflow test scope '{scope.ScopeId}' conflicted during creation but could not be reloaded.");
            return ValueTask.FromResult(WorkflowTestScopeTransitions.Create(Read(winner, scope.ScopeId), scope, createdAt));
        }

        throw new InvalidOperationException(
            $"Groundwork rejected workflow test-scope creation with status '{result.Status}'.");
    }

    public ValueTask<WorkflowTestScopeRecord?> FindAsync(
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2WorkflowTestScopeStorageConventions.PhysicalId(scopeId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Open().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowTestScopeStorageConventions.PhysicalId(scopeId)));
        if (entry is null)
            return ValueTask.FromResult<WorkflowTestScopeRecord?>(null);

        var record = Read(entry, scopeId);
        EnsureTenant(record.Scope.TenantId);
        return ValueTask.FromResult<WorkflowTestScopeRecord?>(record);
    }

    public ValueTask<WorkflowTestScopeCloseResult> CloseAsync(
        WorkflowTestScopeCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowTestScopeStorageConventions.PhysicalId(request.ScopeId));
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = session.Read(key);
            if (existing is null)
                return ValueTask.FromResult(new WorkflowTestScopeCloseResult(
                    WorkflowTestScopeCloseDisposition.NotFound, null));

            var current = Read(existing, request.ScopeId);
            EnsureTenant(current.Scope.TenantId);
            var transition = WorkflowTestScopeTransitions.Close(current, request);
            if (transition.Disposition != WorkflowTestScopeCloseDisposition.Accepted || transition.Record is null)
                return ValueTask.FromResult(transition);

            var result = ConditionalUpsert(
                session,
                GroundworkV2WorkflowTestScopeStorageConventions.Values(transition.Record),
                existing);
            if (IsSaved(result.Status))
                return ValueTask.FromResult(transition);
            if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
                throw new InvalidOperationException(
                    $"Groundwork rejected workflow test-scope closure with status '{result.Status}'.");
        }

        throw new InvalidOperationException(
            $"Workflow test scope '{request.ScopeId}' changed concurrently and did not settle.");
    }

    public ValueTask<WorkflowTestScopeRecord> CompleteAsync(
        string scopeId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2WorkflowTestScopeStorageConventions.PhysicalId(scopeId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowTestScopeStorageConventions.PhysicalId(scopeId));
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = session.Read(key)
                ?? throw new InvalidOperationException("The workflow test scope was not found for closure completion.");
            var current = Read(existing, scopeId);
            EnsureTenant(current.Scope.TenantId);
            var candidate = WorkflowTestScopeTransitions.Complete(current, completedAt);
            if (ReferenceEquals(candidate, current))
                return ValueTask.FromResult(current);

            var result = ConditionalUpsert(
                session,
                GroundworkV2WorkflowTestScopeStorageConventions.Values(candidate),
                existing);
            if (IsSaved(result.Status))
                return ValueTask.FromResult(candidate);
            if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
                throw new InvalidOperationException(
                    $"Groundwork rejected workflow test-scope completion with status '{result.Status}'.");
        }

        throw new InvalidOperationException(
            $"Workflow test scope '{scopeId}' changed concurrently and did not settle.");
    }

    public ValueTask<WorkflowTestScopePage> QueryAsync(
        WorkflowTestScopePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        if (query.PageSize > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(query.PageSize), $"Workflow test-scope pages cannot exceed {MaximumPageSize} rows.");
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(unit.Name);
        var scopeId = Column(table, ElsaRuntimeV2StorageManifest.ScopeIdField);
        var state = Column(table, ElsaRuntimeV2StorageManifest.StateField);
        var expiry = Column(table, ElsaRuntimeV2StorageManifest.ExpiresAtField);
        var predicates = new List<Predicate>();
        if (query.State is { } requestedState)
        {
            predicates.Add(Equal(state, requestedState.ToString()));
        }
        else
        {
            predicates.Add(new Predicate.Or([
                Equal(state, WorkflowTestScopeState.Closing.ToString()),
                new Predicate.And([
                    Equal(state, WorkflowTestScopeState.Open.ToString()),
                    new Predicate.Range(expiry, null, Bound.Inclusive(QueryConstant.Of(expiry, query.ObservedAt)))
                ])]));
        }

        if (query.ContinuationToken is not null)
            predicates.Add(new Predicate.Range(
                scopeId,
                Bound.Exclusive(QueryConstant.Of(scopeId, query.ContinuationToken)),
                null));

        var result = Open().Query(new QueryRequest(
            table,
            Combine(predicates),
            [new OrderTerm(scopeId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(query.PageSize + 1)));
        if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
            throw new InvalidDataException("Groundwork workflow test-scope query returned a continuation after an empty page.");

        var candidates = result.Rows
            .Select(values => GroundworkV2WorkflowTestScopeStorageConventions.Deserialize(values))
            .Where(record => query.State is { } requestedState
                ? record.State == requestedState
                : record.State == WorkflowTestScopeState.Closing ||
                  record.State == WorkflowTestScopeState.Open && record.Scope.IsExpired(query.ObservedAt))
            .OrderBy(record => record.Scope.ScopeId, StringComparer.Ordinal)
            .ToArray();
        foreach (var record in candidates)
            EnsureTenant(record.Scope.TenantId);

        var items = candidates.Take(query.PageSize).ToArray();
        return ValueTask.FromResult(new WorkflowTestScopePage(
            items,
            candidates.Length > query.PageSize ? items[^1].Scope.ScopeId : null));
    }

    public ValueTask AssertOpenAsync(
        WorkflowTestScope scope,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (observedAt == default)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTenant(scope.TenantId);

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowTestScopeStorageConventions.PhysicalId(scope.ScopeId));
        var existing = session.Read(key);
        if (existing is null)
            throw new TestScopeAdmissionException("The workflow test scope is not open in the current persistence context.");

        var record = Read(existing, scope.ScopeId);
        EnsureTenant(record.Scope.TenantId);
        if (record.State != WorkflowTestScopeState.Open ||
            record.Scope.IsExpired(observedAt) ||
            !WorkflowTestScope.ContextEquals(record.Scope, scope))
        {
            throw new TestScopeAdmissionException("The workflow test scope is not open in the current persistence context.");
        }

        var result = ConditionalUpsert(
            session,
            GroundworkV2WorkflowTestScopeStorageConventions.Values(record),
            existing);
        if (result.Status == WriteOutcomeStatus.ConcurrencyConflict)
            throw new TestScopeAdmissionException("The workflow test scope changed during admission.");
        if (!IsSaved(result.Status))
            throw new InvalidOperationException(
                $"Groundwork rejected workflow test-scope admission with status '{result.Status}'.");
        return ValueTask.CompletedTask;
    }

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException("Groundwork workflow test-scope persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
            throw new InvalidOperationException(
                "Groundwork workflow test scopes require one explicit persistence scope; global and across-scope access are refused.");

        return sessions.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope.Value)),
            targetName);
    }

    private void EnsureTenant(string? tenantId) => accessContextAccessor.Current.EnsureTenantScope(tenantId);

    private static WorkflowTestScopeRecord Read(StoredEntry entry, string requestedScopeId)
    {
        var record = GroundworkV2WorkflowTestScopeStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(record.Scope.ScopeId, requestedScopeId))
            throw new InvalidDataException(
                $"Groundwork workflow test-scope physical identity collision detected for '{requestedScopeId}'.");
        return record;
    }

    private static WriteOutcome ConditionalUpsert(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing)
    {
        if (session is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic workflow test-scope concurrency.");
        var revision = existing.Version ?? throw new InvalidDataException(
            "Groundwork workflow test-scope row did not return an optimistic revision.");
        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork workflow test-scope unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            _ => throw new InvalidOperationException(
                $"Groundwork workflow test-scope query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Predicate Combine(IReadOnlyList<Predicate> predicates) => predicates.Count switch
    {
        0 => Predicate.AlwaysTrue.Instance,
        1 => predicates[0],
        _ => new Predicate.And(predicates)
    };

    private static Predicate Equal(ColumnRef column, string value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static bool IsSaved(WriteOutcomeStatus status) => status is
        WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;
}
