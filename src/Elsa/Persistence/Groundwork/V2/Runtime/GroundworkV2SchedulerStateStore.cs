using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 scheduler-state store.</summary>
public sealed class GroundworkV2SchedulerStateStore : ISchedulerStateStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2SchedulerStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind, targetName);
    }

    public ValueTask<SchedulerState> SaveAsync(
        SchedulerState state,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2SchedulerStateStorageConventions.Validate(state);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(state.WorkflowExecutionId);
        var values = GroundworkV2SchedulerStateStorageConventions.Values(state);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, state.WorkflowExecutionId)
            : session.Insert(values, WriteOptions.CreateOnly);
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork scheduler-state save lost a concurrent write; retry the operation.");
        }

        return ValueTask.FromResult(state);
    }

    public ValueTask<SchedulerState?> FindAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Open().Read(GroundworkRuntimeRowStore.Key(workflowExecutionId));
        if (entry is null)
            return ValueTask.FromResult<SchedulerState?>(null);

        var state = GroundworkV2SchedulerStateStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(state, workflowExecutionId);
        return ValueTask.FromResult<SchedulerState?>(state);
    }

    public ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var table = new TableId(unit.Name);
        var collection = Column(table, ElsaRuntimeV2StorageManifest.CollectionField);
        var id = Column(table, ElsaRuntimeV2StorageManifest.IdField);
        var where = new Predicate.Equal(
            collection,
            QueryConstant.Of(collection, ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind));
        var states = new List<SchedulerState>();
        string? cursor = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new QueryRequest(
                table,
                where,
                [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                PagingFor(RuntimeStorePageRequest.MaximumLimit, cursor));
            var result = session.Query(request);
            states.AddRange(result.Rows.Select(GroundworkV2SchedulerStateStorageConventions.Deserialize));
            if (result.NextContinuationToken is { } next && StringComparer.Ordinal.Equals(next, cursor))
            {
                throw new InvalidDataException("Groundwork scheduler-state continuation did not advance.");
            }

            cursor = result.NextContinuationToken;
        } while (cursor is not null);

        return ValueTask.FromResult<IReadOnlyCollection<SchedulerState>>(states);
    }

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException(
                          "Groundwork scheduler-state persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork scheduler-state requires one explicit persistence scope; global and across-scope access are refused.");
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
        string workflowExecutionId)
    {
        var previous = GroundworkV2SchedulerStateStorageConventions.Deserialize(existing.Values.Values);
        EnsureIdentity(previous, workflowExecutionId);
        var version = existing.Version ?? throw new InvalidDataException(
            "Groundwork scheduler-state row did not return an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic scheduler-state concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(version));
    }

    private static void EnsureIdentity(SchedulerState state, string expectedId)
    {
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, expectedId))
        {
            throw new InvalidDataException(
                "Groundwork scheduler-state row identity does not match its requested key.");
        }
    }

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork scheduler-state unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork scheduler-state query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);
}
