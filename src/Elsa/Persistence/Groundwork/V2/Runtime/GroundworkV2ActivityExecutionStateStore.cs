using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 activity-execution state store.</summary>
public sealed class GroundworkV2ActivityExecutionStateStore : GroundworkV2RuntimeStoreBase, IActivityExecutionStateStore
{

    public GroundworkV2ActivityExecutionStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
        : base(sessions, accessContextAccessor, targetName, "activity-execution state", ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind)
    {
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

        var table = new TableId(Unit.Name);
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

        var table = new TableId(Unit.Name);
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

    private WriteOutcome UpdateExisting(
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
        return ConditionalUpsert(session, values, revision);
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
}
