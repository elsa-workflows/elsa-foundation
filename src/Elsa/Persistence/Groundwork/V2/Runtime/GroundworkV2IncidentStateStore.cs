using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 incident-state store.</summary>
/// <remarks>
/// Incident identities are composite workflow-execution/incident identities. All reads require one
/// explicit persistence scope and all collection reads use provider-owned bounded keyset pages.
/// </remarks>
public sealed class GroundworkV2IncidentStateStore : IIncidentStateStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2IncidentStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind, targetName);
    }

    public ValueTask<bool> TryAddAsync(
        IncidentState state,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2IncidentStateStorageConventions.Validate(state);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2IncidentStateStorageConventions.PhysicalId(
                state.WorkflowExecutionId,
                state.IncidentId));
        if (session.Read(key) is { } existing)
        {
            EnsureIdentity(existing, state.WorkflowExecutionId, state.IncidentId);
            return ValueTask.FromResult(false);
        }

        var result = session.Insert(
            GroundworkV2IncidentStateStorageConventions.Values(state),
            WriteOptions.CreateOnly);
        if (result.Status == WriteOutcomeStatus.Inserted)
            return ValueTask.FromResult(true);
        if (result.Status == WriteOutcomeStatus.ConcurrencyConflict)
        {
            var winner = session.Read(key) ?? throw new InvalidOperationException(
                $"Incident '{state.IncidentId}' conflicted during creation but could not be reloaded.");
            EnsureIdentity(winner, state.WorkflowExecutionId, state.IncidentId);
            return ValueTask.FromResult(false);
        }

        throw new InvalidOperationException(
            $"Groundwork rejected incident '{state.IncidentId}' with status '{result.Status}'.");
    }

    public ValueTask<IncidentState> SaveAsync(
        IncidentState state,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2IncidentStateStorageConventions.Validate(state);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var physicalId = GroundworkV2IncidentStateStorageConventions.PhysicalId(
            state.WorkflowExecutionId,
            state.IncidentId);
        var key = GroundworkRuntimeRowStore.Key(physicalId);
        var values = GroundworkV2IncidentStateStorageConventions.Values(state);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, state)
            : session.Insert(values, WriteOptions.CreateOnly);

        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork incident-state save lost a concurrent write; retry the operation.");
        }

        return ValueTask.FromResult(state);
    }

    public ValueTask<IncidentState?> FindAsync(
        string workflowExecutionId,
        string incidentId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, incidentId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Open().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2IncidentStateStorageConventions.PhysicalId(workflowExecutionId, incidentId)));
        if (entry is null)
            return ValueTask.FromResult<IncidentState?>(null);

        var state = GroundworkV2IncidentStateStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(state, workflowExecutionId, incidentId);
        return ValueTask.FromResult<IncidentState?>(state);
    }

    public ValueTask<int> CountAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var incident = Column(table, ElsaRuntimeV2StorageManifest.IncidentIdField);
        var result = Open().Query(new QueryRequest(
            table,
            Equal(workflow, workflowExecutionId),
            [new OrderTerm(incident, OrderDirection.Ascending, NullOrder.Last)],
            Projection.ColumnsOnly(incident),
            Paging.Keyset(1),
            ResultShape.TotalCount.Instance));
        var count = result.TotalCount ?? throw new InvalidDataException(
            "Groundwork incident-state count did not return its provider-side total.");
        return ValueTask.FromResult(count > int.MaxValue ? int.MaxValue : (int)count);
    }

    public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<IncidentState>>(
            QueryAll(workflowExecutionId, status: null, cancellationToken));
    }

    public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<IncidentState>>(
            QueryAll(workflowExecutionId, IncidentStatus.Blocking, cancellationToken));
    }

    private IReadOnlyCollection<IncidentState> QueryAll(
        string workflowExecutionId,
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        var session = Open();
        var table = new TableId(unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var incident = Column(table, ElsaRuntimeV2StorageManifest.IncidentIdField);
        var predicates = new List<Predicate> { Equal(workflow, workflowExecutionId) };
        if (status is { } filterStatus)
        {
            var statusColumn = Column(table, ElsaRuntimeV2StorageManifest.StatusField);
            predicates.Add(Equal(statusColumn, filterStatus.ToString()));
        }

        var rows = new List<IncidentState>();
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                table,
                Combine(predicates),
                [new OrderTerm(incident, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                PagingFor(RuntimeStorePageRequest.MaximumLimit, cursor)));
            rows.AddRange(result.Rows.Select(GroundworkV2IncidentStateStorageConventions.Deserialize));
            if (result.NextContinuationToken is { } next && !seenContinuations.Add(next))
            {
                throw new InvalidDataException(
                    "Groundwork incident-state continuation repeated or cycled.");
            }

            cursor = result.NextContinuationToken;
        } while (cursor is not null);

        foreach (var state in rows)
        {
            EnsureIdentity(state, workflowExecutionId, state.IncidentId);
            if (status.HasValue && state.Status != status.Value)
            {
                throw new InvalidDataException(
                    "Groundwork incident-state status projection does not match its current content.");
            }
        }

        return rows;
    }

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException(
                          "Groundwork incident-state persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork incident state requires one explicit persistence scope; global and across-scope access are refused.");
        }

        return sessions.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope.Value)),
            targetName);
    }

    private static WriteOutcome UpdateExisting(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing,
        IncidentState state)
    {
        var previous = GroundworkV2IncidentStateStorageConventions.Deserialize(existing.Values.Values);
        EnsureIdentity(previous, state.WorkflowExecutionId, state.IncidentId);
        IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(previous, state);
        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork incident '{state.IncidentId}' did not return an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic incident-state concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private static void ValidateIdentity(string workflowExecutionId, string incidentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        _ = GroundworkV2IncidentStateStorageConventions.PhysicalId(workflowExecutionId, incidentId);
    }

    private static void EnsureIdentity(
        StoredEntry existing,
        string workflowExecutionId,
        string incidentId) =>
        EnsureIdentity(
            GroundworkV2IncidentStateStorageConventions.Deserialize(existing.Values.Values),
            workflowExecutionId,
            incidentId);

    private static void EnsureIdentity(
        IncidentState state,
        string workflowExecutionId,
        string incidentId)
    {
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(state.IncidentId, incidentId))
        {
            throw new InvalidDataException(
                "Groundwork incident-state row identity does not match its requested key.");
        }
    }

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork incident-state unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork incident-state query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Predicate Equal(ColumnRef column, string value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate Combine(IReadOnlyList<Predicate> predicates) => predicates.Count switch
    {
        0 => Predicate.AlwaysTrue.Instance,
        1 => predicates[0],
        _ => new Predicate.And(predicates)
    };

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;
}
