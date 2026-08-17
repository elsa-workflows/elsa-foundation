using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 activity-execution inspection store.</summary>
/// <remarks>
/// Inspection rows use the same length-prefixed workflow/activity identity as the checkpoint writer.
/// Reads and writes are limited to one explicit persistence scope and all list queries are provider-owned
/// bounded keyset queries.
/// </remarks>
public sealed class GroundworkV2ActivityExecutionInspectionStore : IActivityExecutionInspectionStore, IActivityExecutionInspectionWriter
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2ActivityExecutionInspectionStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind, targetName);
    }

    public ValueTask SaveAsync(
        ActivityExecutionInspectionProjection projection,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2ActivityExecutionInspectionStorageConventions.Validate(projection);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var physicalId = GroundworkV2ActivityExecutionInspectionStorageConventions.PhysicalId(
            projection.WorkflowExecutionId,
            projection.ActivityExecutionId);
        var key = GroundworkRuntimeRowStore.Key(physicalId);
        var values = GroundworkV2ActivityExecutionInspectionStorageConventions.Values(projection);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, projection)
            : session.Insert(values, WriteOptions.CreateOnly);
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork activity-execution inspection save lost a concurrent write; retry the operation.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<ActivityExecutionInspectionProjection?> FindAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Open().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2ActivityExecutionInspectionStorageConventions.PhysicalId(
                workflowExecutionId,
                activityExecutionId)));
        if (entry is null)
            return ValueTask.FromResult<ActivityExecutionInspectionProjection?>(null);

        var projection = GroundworkV2ActivityExecutionInspectionStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(projection, workflowExecutionId, activityExecutionId);
        return ValueTask.FromResult<ActivityExecutionInspectionProjection?>(projection);
    }

    public ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(
        ActivityExecutionInspectionSummaryPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var executionSequence = Column(
            table,
            ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField);
        var scheduledAt = Column(
            table,
            ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryScheduledAtField);
        var activityExecutionId = Column(
            table,
            ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField);
        var request = new QueryRequest(
            table,
            new Predicate.Equal(workflow, QueryConstant.Of(workflow, query.WorkflowExecutionId)),
            [
                new OrderTerm(executionSequence, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(scheduledAt, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(activityExecutionId, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken),
            ResultShape.TotalCount.Instance);
        var result = QueryWithBoundCursor(request, query.ContinuationToken);
        var totalCount = result.TotalCount ?? throw new InvalidDataException(
            "Groundwork activity-execution inspection summary query did not return its provider-side total.");
        var items = result.Rows.Select(values =>
        {
            var projection = GroundworkV2ActivityExecutionInspectionStorageConventions.Deserialize(values);
            EnsureWorkflowIdentity(projection, query.WorkflowExecutionId);
            return ActivityExecutionInspectionSummaryProjection.FromProjection(projection);
        }).ToArray();
        return ValueTask.FromResult(new ActivityExecutionInspectionSummaryPage(
            query,
            items,
            totalCount,
            result.NextContinuationToken));
    }

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException(
                          "Groundwork activity-execution inspection persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork activity-execution inspection requires one explicit persistence scope; " +
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
        ActivityExecutionInspectionProjection projection)
    {
        var previous = GroundworkV2ActivityExecutionInspectionStorageConventions.Deserialize(existing.Values.Values);
        EnsureIdentity(previous, projection.WorkflowExecutionId, projection.ActivityExecutionId);
        var version = existing.Version ?? throw new InvalidDataException(
            "Groundwork activity-execution inspection row did not return an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic activity-execution inspection concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(version));
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
                "The activity-execution inspection continuation token is invalid or does not belong to this query.",
                "continuationToken",
                exception);
        }
    }

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);

    private static void ValidateIdentity(string workflowExecutionId, string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        _ = GroundworkV2ActivityExecutionInspectionStorageConventions.PhysicalId(
            workflowExecutionId,
            activityExecutionId);
    }

    private static void EnsureIdentity(
        ActivityExecutionInspectionProjection projection,
        string workflowExecutionId,
        string activityExecutionId)
    {
        if (!StringComparer.Ordinal.Equals(projection.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(projection.ActivityExecutionId, activityExecutionId))
        {
            throw new InvalidDataException(
                "Groundwork activity-execution inspection row identity does not match its requested key.");
        }
    }

    private static void EnsureWorkflowIdentity(
        ActivityExecutionInspectionProjection projection,
        string workflowExecutionId)
    {
        if (!StringComparer.Ordinal.Equals(projection.WorkflowExecutionId, workflowExecutionId))
        {
            throw new InvalidDataException(
                "Groundwork activity-execution inspection row workflow identity does not match its requested query.");
        }
    }

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork activity-execution inspection unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork activity-execution inspection query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;
}
