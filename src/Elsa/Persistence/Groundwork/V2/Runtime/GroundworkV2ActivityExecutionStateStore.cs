using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 activity-execution state store.</summary>
public sealed class GroundworkV2ActivityExecutionStateStore : IActivityExecutionStateStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2ActivityExecutionStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind, targetName);
    }

    public ValueTask<ActivityExecutionState> SaveAsync(
        ActivityExecutionState state,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2ActivityExecutionStorageConventions.Validate(state);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var physicalId = GroundworkV2ActivityExecutionStorageConventions.PhysicalId(
            state.Execution.WorkflowExecutionId,
            state.Execution.ActivityExecutionId);
        var key = GroundworkRuntimeRowStore.Key(physicalId);
        var values = GroundworkV2ActivityExecutionStorageConventions.Values(state);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, state)
            : session.Insert(values, WriteOptions.CreateOnly);
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork activity-execution save lost a concurrent write; retry the operation.");
        }

        return ValueTask.FromResult(state);
    }

    public ValueTask<ActivityExecutionState?> FindAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Open().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2ActivityExecutionStorageConventions.PhysicalId(
                workflowExecutionId,
                activityExecutionId)));
        if (entry is null)
            return ValueTask.FromResult<ActivityExecutionState?>(null);

        var state = GroundworkV2ActivityExecutionStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(state, workflowExecutionId, activityExecutionId);
        return ValueTask.FromResult<ActivityExecutionState?>(state);
    }

    public ValueTask<long> CountAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(unit.Name);
        var activityId = Column(table, ElsaRuntimeV2StorageManifest.ActivityExecutionIdField);
        var request = new QueryRequest(
            table,
            Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField), workflowExecutionId),
            [new OrderTerm(activityId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.ColumnsOnly(activityId),
            Paging.Keyset(1),
            ResultShape.TotalCount.Instance);
        var result = Open().Query(request);
        return ValueTask.FromResult(result.TotalCount ?? throw new InvalidDataException(
            "Groundwork activity-execution count did not return its provider-side total."));
    }

    public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(
        ActivityExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return QueryPage(query, query.WorkflowExecutionId, parentActivityExecutionId: null, cancellationToken);
    }

    public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(
        ActivityExecutionStateParentPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return QueryPage(query, query.WorkflowExecutionId, query.ParentActivityExecutionId, cancellationToken);
    }

    private ValueTask<RuntimeStorePage<ActivityExecutionState>> QueryPage(
        RuntimeStorePageRequest query,
        string workflowExecutionId,
        string? parentActivityExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(unit.Name);
        var predicates = new List<Predicate>
        {
            Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField), workflowExecutionId)
        };
        if (parentActivityExecutionId is not null)
        {
            predicates.Add(Equal(
                Column(table, ElsaRuntimeV2StorageManifest.ParentActivityExecutionIdField),
                parentActivityExecutionId));
        }

        var activityId = Column(table, ElsaRuntimeV2StorageManifest.ActivityExecutionIdField);
        var request = new QueryRequest(
            table,
            Combine(predicates),
            [new OrderTerm(activityId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken));
        var result = QueryWithBoundCursor(request, query.ContinuationToken);
        var items = result.Rows.Select(values =>
        {
            var state = GroundworkV2ActivityExecutionStorageConventions.Deserialize(values);
            if (!StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId))
            {
                throw new InvalidDataException(
                    "Groundwork activity-execution row workflow projection does not match its requested workflow.");
            }

            if (parentActivityExecutionId is not null &&
                !StringComparer.Ordinal.Equals(state.ParentActivityExecutionId, parentActivityExecutionId))
            {
                throw new InvalidDataException(
                    "Groundwork activity-execution row parent projection does not match its current content.");
            }

            return state;
        }).ToArray();
        return ValueTask.FromResult(new RuntimeStorePage<ActivityExecutionState>(query, items, result.NextContinuationToken));
    }

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException(
                          "Groundwork activity-execution persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork activity-execution state requires one explicit persistence scope; " +
                "global and across-scope access are refused.");
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
        ActivityExecutionState state)
    {
        var previous = GroundworkV2ActivityExecutionStorageConventions.Deserialize(existing.Values.Values);
        EnsureIdentity(
            previous,
            state.Execution.WorkflowExecutionId,
            state.Execution.ActivityExecutionId);
        var revision = existing.Version ?? throw new InvalidDataException(
            "Groundwork activity-execution row did not return an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic activity-execution concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private QueryMaterializedResult QueryWithBoundCursor(QueryRequest request, string? cursor)
    {
        try
        {
            return Open().Query(request);
        }
        catch (Exception exception) when (
            cursor is not null &&
            (exception is QueryRenderException { Code: "GW-QUERY-013" } ||
             exception is FormatException ||
             exception.InnerException is FormatException))
        {
            throw new ArgumentException(
                "The activity-execution continuation token is invalid or does not belong to this query.",
                "continuationToken",
                exception);
        }
    }

    private static void ValidateIdentity(string workflowExecutionId, string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        _ = GroundworkV2ActivityExecutionStorageConventions.PhysicalId(workflowExecutionId, activityExecutionId);
    }

    private static void EnsureIdentity(
        ActivityExecutionState state,
        string workflowExecutionId,
        string activityExecutionId)
    {
        if (!StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, activityExecutionId))
        {
            throw new InvalidDataException(
                "Groundwork activity-execution row identity does not match its requested key.");
        }
    }

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork activity-execution unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork activity-execution query column '{name}' has unsupported type '{definition.Type}'.")
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
